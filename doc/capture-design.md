# スクリーンショットキャプチャ設計

## 1. 設計方針

スクリーンショット機能を **共有ライブラリ + CLI ラッパー** で構成する。

```
WinFormsTestHarness.Capture (classlib)
  ← 撮影ロジック、差分検知、before/after制御のコアライブラリ

wfth-record (console)
  ← --capture オプションで内部的にライブラリを使用
  ← 入力フックと同一プロセス内でbefore/afterが正確

wfth-capture (console)
  ← ライブラリのCLIラッパー
  ← ポーリングモード、手動撮影、デバッグ用途
```

### 利用パターン

```
パターンA — Recording中の自動撮影（主要ユースケース）:
  wfth-record --process SampleApp --capture --capture-level 2
  → 入力イベントに連動して自動撮影、NDJSON に統合出力

パターンB — 独立した定期撮影:
  wfth-capture --process SampleApp --interval 1000
  → 1秒間隔で定期撮影、差分検知で無変化時スキップ

パターンC — 手動スナップショット:
  wfth-capture --process SampleApp --once
  → 1回だけ撮影して終了（デバッグ用）
```

---

## 2. ライブラリ設計 — WinFormsTestHarness.Capture

### 2.1 コアAPI

```csharp
/// <summary>ウィンドウのスクリーンショットを撮影する</summary>
public class ScreenCapturer : IDisposable
{
    public ScreenCapturer(IntPtr hwnd, CaptureOptions options);

    /// <summary>即座に1枚撮影</summary>
    public CaptureResult Capture(string? triggeredBy = null);

    /// <summary>ウィンドウ領域を取得</summary>
    public Rectangle GetWindowRect();
}

public class CaptureOptions
{
    /// <summary>品質: low|medium|high|full</summary>
    public CaptureQuality Quality { get; set; } = CaptureQuality.Medium;

    /// <summary>最大幅（px）。0 = 制限なし</summary>
    public int MaxWidth { get; set; } = 0;

    /// <summary>出力形式</summary>
    public ImageFormat Format { get; set; } = ImageFormat.Png;

    /// <summary>JPEG品質（Format=Jpeg時のみ、1-100）</summary>
    public int JpegQuality { get; set; } = 70;
}

public enum CaptureQuality
{
    Low,     // 元画像の50% + JPEG 70%
    Medium,  // 元画像の75% + PNG
    High,    // 元画像そのまま + PNG
    Full     // 元画像そのまま + PNG無圧縮
}
```

### 2.2 差分検知

```csharp
/// <summary>前回のスクリーンショットとの差分を検知する</summary>
public class DiffDetector
{
    private byte[]? _previousHash;

    /// <summary>閾値（0.0〜1.0）。デフォルト 0.02 = 2%</summary>
    public double Threshold { get; set; } = 0.02;

    /// <summary>変化があったか判定</summary>
    public bool HasChanged(Bitmap current);

    /// <summary>差分率を計算（0.0〜1.0）</summary>
    public double CalculateDiffRatio(Bitmap current);
}
```

```
差分検知アルゴリズム:
  1. 元画像を 64x48 にリサイズ（高速比較用サムネイル）
  2. 各ピクセルのRGB値を比較
  3. 差異ピクセル数 / 全ピクセル数 = 差分率
  4. 差分率 > 閾値 → 変化あり

最適化:
  - サムネイルのハッシュ値を保持し、完全一致なら即座にスキップ
  - ピクセル比較は輝度のみ（グレースケール化）で高速化
```

### 2.3 撮影戦略（before/after制御）

```csharp
/// <summary>入力イベント連動の撮影戦略</summary>
public class CaptureStrategy
{
    private readonly ScreenCapturer _capturer;
    private readonly DiffDetector _diffDetector;
    private readonly CaptureLevel _level;
    private CaptureResult? _lastCapture;

    /// <summary>入力イベント発生前に呼ぶ</summary>
    public CaptureResult? CaptureBeforeInput(string eventDescription)
    {
        if (_level < CaptureLevel.BeforeAfter)
            return null;

        // 直前のafterと同一なら撮影スキップ
        if (_lastCapture != null)
        {
            var current = _capturer.Capture($"before:{eventDescription}");
            if (!_diffDetector.HasChanged(current.Bitmap))
            {
                current.Skipped = true;
                current.ReuseFrom = _lastCapture.FilePath;
                return current;
            }
            return current;
        }

        return _capturer.Capture($"before:{eventDescription}");
    }

    /// <summary>入力イベント発生後に呼ぶ（UI反応待ち付き）</summary>
    public async Task<CaptureResult?> CaptureAfterInputAsync(
        string eventDescription,
        int delayMs = 300)
    {
        if (_level < CaptureLevel.AfterOnly)
            return null;

        // UIの反応を待つ
        await Task.Delay(delayMs);

        var result = _capturer.Capture($"after:{eventDescription}");

        if (!_diffDetector.HasChanged(result.Bitmap))
        {
            result.Skipped = true;
            return result;
        }

        _lastCapture = result;
        return result;
    }
}

public enum CaptureLevel
{
    None = 0,         // 撮影しない
    AfterOnly = 1,    // 操作後のみ（変化時のみ）
    BeforeAfter = 2,  // 操作前後（変化時のみ）
    All = 3           // 全操作の前後を無条件に撮影
}
```

### 2.4 撮影結果

```csharp
public class CaptureResult
{
    public DateTimeOffset Timestamp { get; set; }
    public string TriggeredBy { get; set; } = "";
    public string FilePath { get; set; } = "";      // 保存先パス
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }               // バイト
    public double? DiffRatio { get; set; }            // 前回との差分率
    public bool Skipped { get; set; }                 // 差分なしでスキップ
    public string? ReuseFrom { get; set; }            // スキップ時の参照先
    public Bitmap Bitmap { get; set; } = null!;       // 内部保持（保存後にDispose）
}
```

### 2.5 ファイル保存

```csharp
/// <summary>スクリーンショットをファイルに保存</summary>
public class CaptureFileWriter
{
    private readonly string _outputDir;
    private int _sequenceNumber;

    /// <summary>連番ファイル名で保存</summary>
    public string Save(CaptureResult result, string suffix)
    {
        // suffix: "before", "after", "periodic", "manual"
        var seq = Interlocked.Increment(ref _sequenceNumber);
        var fileName = $"{seq:D4}_{suffix}.png";
        var filePath = Path.Combine(_outputDir, fileName);

        result.Bitmap.Save(filePath, GetEncoder(result));
        result.FilePath = filePath;
        result.FileSize = new FileInfo(filePath).Length;

        return filePath;
    }
}
```

---

## 3. wfth-record への統合

### 3.1 CLIオプション追加

```
wfth-record [既存オプション]

Capture Options:
  --capture              スクリーンショット撮影を有効化
  --capture-level <n>    撮影レベル 0|1|2|3（デフォルト: 1）
  --capture-quality <q>  low|medium|high|full（デフォルト: medium）
  --capture-dir <dir>    保存ディレクトリ（デフォルト: ./screenshots）
  --capture-delay <ms>   after撮影の待機時間（デフォルト: 300）
  --diff-threshold <pct> 差分検知閾値パーセント（デフォルト: 2）
```

### 3.2 統合フロー

```
wfth-record --process SampleApp --capture --capture-level 2

[Hook Callback Thread]          [Writer Thread]          [Capture]
  │                               │                       │
  ├─ MouseDown 検知               │                       │
  ├─ Queue.Enqueue(event)         │                       │
  │                               ├─ Dequeue(event)       │
  │                               ├─ ★ CaptureBeforeInput()
  │                               │                       ├─ 撮影
  │                               │                       ├─ 差分判定
  │                               │                       └─ 保存
  │                               ├─ 出力: mouse event     │
  │                               ├─ 出力: screenshot event│
  │                               │                       │
  ├─ MouseUp 検知                 │                       │
  ├─ Queue.Enqueue(event)         │                       │
  │                               ├─ Dequeue(event)       │
  │                               ├─ 出力: mouse event     │
  │                               ├─ ★ await CaptureAfterInputAsync(300ms)
  │                               │                       ├─ 300ms 待機
  │                               │                       ├─ 撮影
  │                               │                       ├─ 差分判定
  │                               │                       └─ 保存
  │                               ├─ 出力: screenshot event│
```

### 3.3 NDJSON出力形式

入力イベントとスクリーンショットイベントが時系列で混在する。

```json
{"ts":"...","type":"mouse","action":"LeftDown","sx":450,"sy":320,"rx":230,"ry":180}
{"ts":"...","type":"screenshot","timing":"before","file":"screenshots/0001_before.png","w":1024,"h":768,"size":245760,"diff":0.15,"trigger":"mouse_LeftDown"}
{"ts":"...","type":"mouse","action":"LeftUp","sx":450,"sy":320,"rx":230,"ry":180}
{"ts":"...","type":"screenshot","timing":"after","file":"screenshots/0001_after.png","w":1024,"h":768,"size":251904,"diff":0.08,"trigger":"mouse_LeftUp"}
{"ts":"...","type":"key","action":"down","vk":84,"key":"T","scan":20,"char":"T"}
{"ts":"...","type":"key","action":"up","vk":84,"key":"T","scan":20}
{"ts":"...","type":"screenshot","timing":"after","file":"screenshots/0002_after.png","w":1024,"h":768,"size":248832,"diff":0.03,"trigger":"key_input_idle"}
```

差分なしでスキップした場合:

```json
{"ts":"...","type":"screenshot","timing":"after","skipped":true,"diff":0.01,"trigger":"mouse_LeftUp"}
```

### 3.4 パイプライン変更

```bash
# 変更前（3プロセス）:
wfth-record  --process SampleApp               > $SESSION/input.ndjson &
wfth-capture --process SampleApp --watch-file ... > $SESSION/capture.ndjson &
wfth-inspect watch --process SampleApp          > $SESSION/uia.ndjson &

# 変更後（2プロセス）:
wfth-record  --process SampleApp --capture      > $SESSION/record.ndjson &
wfth-inspect watch --process SampleApp          > $SESSION/uia.ndjson &

# correlate:
wfth-aggregate < $SESSION/record.ndjson \
  | wfth-correlate --uia $SESSION/uia.ndjson \
                   --screenshots $SESSION/screenshots \
  > $SESSION/session.ndjson
```

入力イベントとスクリーンショットは `record.ndjson` に統合されるが、`wfth-correlate` は `wfth-aggregate` の出力（stdin）を正規入力とする。
スクリーンショットファイルは `--screenshots` で明示的に渡す。

---

## 4. wfth-capture（スタンドアロンCLI）

### 4.1 CLIインターフェース

```
wfth-capture [options]

Target:
  --process <name>       プロセス名
  --hwnd <handle>        ウィンドウハンドル

Mode（いずれか1つ）:
  --once                 1回だけ撮影して終了
  --interval <ms>        定期撮影（デフォルト: 1000）
  --watch-file <path>    NDJSONファイルを監視してトリガー
  --watch-stdin          stdinからのイベント行でトリガー

Capture:
  --quality <q>          low|medium|high|full（デフォルト: medium）
  --diff-threshold <pct> 差分検知閾値パーセント（デフォルト: 2）
  --no-diff              差分検知を無効にする（全撮影）

Output:
  --out-dir <dir>        スクリーンショット保存先（デフォルト: ./screenshots）
  --out <path>           メタデータNDJSON出力先（デフォルト: stdout）
```

### 4.2 使用例

```bash
# デバッグ: 今の画面を1枚撮影
wfth-capture --process SampleApp --once --out-dir ./debug

# 変化監視: 1秒間隔で撮影、変化時のみ保存
wfth-capture --process SampleApp --interval 1000 > captures.ndjson

# パイプラインの部品として（wfth-record未使用時）
wfth-capture --process SampleApp --watch-stdin < events.ndjson > captures.ndjson
```

---

## 5. プロジェクト構成の変更

### 5.1 変更点

```
変更前:
  src/WinFormsTestHarness.Capture/  ← console (スケルトン)

変更後:
  src/WinFormsTestHarness.Capture/  ← classlib（コアライブラリ）
      ├── ScreenCapturer.cs
      ├── DiffDetector.cs
      ├── CaptureStrategy.cs
      ├── CaptureResult.cs
      ├── CaptureFileWriter.cs
      └── CaptureOptions.cs

  src/WinFormsTestHarness.Capture.Cli/  ← console（スタンドアロンCLI）
      ├── Program.cs
      └── WinFormsTestHarness.Capture.Cli.csproj
          → PackAsTool, ToolCommandName=wfth-capture
          → ProjectReference: WinFormsTestHarness.Capture

  src/WinFormsTestHarness.Record/  ← console（既存）
      → ProjectReference: WinFormsTestHarness.Capture
      → --capture オプション実装で CaptureStrategy を使用
```

### 5.2 csproj

```xml
<!-- WinFormsTestHarness.Capture.csproj — classlib に変更 -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <!-- OutputType なし = classlib -->
  </PropertyGroup>
</Project>
```

```xml
<!-- WinFormsTestHarness.Capture.Cli.csproj — 新規 -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>wfth-capture</ToolCommandName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\WinFormsTestHarness.Capture\WinFormsTestHarness.Capture.csproj" />
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
  </ItemGroup>
</Project>
```

### 5.3 wfth-correlate の入力契約変更

```
変更前:
  wfth-correlate --input <path> --capture <path> --uia <path>

変更後:
  wfth-aggregate < record.ndjson \
    | wfth-correlate --uia <path> --screenshots <dir>

  correlate は stdin から受けた集約済みアクションを基準に
  UIA変化・スクリーンショットを時間窓で紐付ける
```

### 5.4 セッションディレクトリ規約の更新

```
変更前:
  sessions/rec-.../
  ├── input.ndjson
  ├── capture.ndjson
  ├── uia.ndjson
  ├── screenshots/
  └── session.json

変更後:
  sessions/rec-.../
  ├── record.ndjson     ← 入力イベント + スクリーンショットメタデータ
  ├── uia.ndjson        ← UIAツリー変化
  ├── screenshots/      ← PNG ファイル
  └── session.ndjson    ← 統合ログ（NDJSON）
```

---

## 6. 画面遷移の自動検出

### 6.1 定期スキャン

CaptureStrategy はイベント駆動の撮影に加えて、バックグラウンドで定期的な差分チェックを行う。

```csharp
public class TransitionDetector
{
    private readonly ScreenCapturer _capturer;
    private readonly DiffDetector _diffDetector;

    /// <summary>画面遷移の閾値（通常の差分閾値より高い）</summary>
    public double TransitionThreshold { get; set; } = 0.30;

    /// <summary>チェック間隔</summary>
    public int IntervalMs { get; set; } = 500;

    public event EventHandler<CaptureResult>? TransitionDetected;

    public async Task MonitorAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(IntervalMs, ct);
            var result = _capturer.Capture("periodic_scan");
            var diffRatio = _diffDetector.CalculateDiffRatio(result.Bitmap);

            if (diffRatio > TransitionThreshold)
            {
                // 30% 以上の変化 → 画面遷移と判断
                TransitionDetected?.Invoke(this, result);
            }

            result.Bitmap.Dispose();
        }
    }
}
```

出力:

```json
{"ts":"...","type":"screenshot","timing":"transition","file":"screenshots/transition_0005.png","w":1024,"h":768,"diff":0.45,"trigger":"periodic_scan"}
```

wfth-correlate はこの transition イベントを検出し、入力イベントに紐づかない画面変化（タイマー遷移、非同期処理の完了等）として記録する。

---

## 7. 実装優先度

| 機能 | MVP段階 | 理由 |
|------|---------|------|
| ScreenCapturer (基本撮影) | **MVP C** | コア機能 |
| DiffDetector | **MVP C** | 容量削減の基本 |
| CaptureFileWriter | **MVP C** | ファイル保存 |
| wfth-record --capture 統合 | **MVP C** | 主要ユースケース |
| wfth-capture --once | **MVP C** | デバッグに最低限必要 |
| CaptureStrategy (before/after) | MVP C+ | Level 2 撮影 |
| wfth-capture --interval | MVP C+ | 独立撮影 |
| TransitionDetector | 将来 | あると便利だが必須ではない |
| wfth-capture --watch-file/stdin | 将来 | レガシーパイプラインとの互換性 |
