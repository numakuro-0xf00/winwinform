# WinFormsTestHarness.Logger アーキテクチャ設計

## 1. 概要

WinFormsTestHarness.Logger は、WinForms アプリに組み込む NuGet パッケージ形式のインストルメンテーション・ロガーである。Recording Engine（外部プロセス）に対して名前付きパイプ経由でアプリ内部のイベント（クリック、テキスト変更、フォーム開閉等）をリアルタイム送信する。

`#if E2E_TEST` プリプロセッサディレクティブにより、シンボル未定義のビルドではロガーのコードが IL に含まれない。CI では `dotnet build -c Release -p:E2ETestEnabled=true` で Release 最適化付きの Logger 有効ビルドが可能。

### 参考: VibeLogger

過去に作成した [VibeLogger](https://github.com/numakuro-0xf00/vibelogger_csharp) のパターンを参考にしている。

| 再利用するパターン | 内容 |
|------------------|------|
| ThreadSafeContainer | ConcurrentQueue + 自動トリムによる安全なログバッファ |
| BufferedWriter | バッファリング + タイマーフラッシュによる効率的な書き込み |
| No-Throw 設計 | すべての例外をスワロー、ホストアプリへの影響をゼロに |
| コンテキストサニタイズ | Delegate/Type の除外、長い値のトランケート |
| ファイルローテーション | サイズ閾値 + SemaphoreSlim 排他による安全なローテーション |

---

## 2. 設計方針

- **No-Throw 保証**: ロガーがホストアプリをクラッシュさせることは絶対にない
- **UI スレッド最小負荷**: イベントハンドラは `ConcurrentQueue.Enqueue` のみ（ロックフリー、O(1)）
- **Fire-and-Forget**: シリアライズ・I/O はすべてバックグラウンドスレッドで実行
- **フォールバック**: IPC 切断時はローカルファイルに自動フォールバック
- **ビルド構成非依存**: `E2E_TEST` シンボルの有無でロガーの有効/無効を制御。ビルド構成（Release/Debug/E2ETest）に縛られない

---

## 3. 条件付きコンパイル戦略

### 3.1 `#if E2E_TEST` 方式の採用

`[Conditional("E2E_TEST")]` 属性ではなく `#if E2E_TEST` / `#endif` プリプロセッサディレクティブを採用する。

#### 理由

| 観点 | `[Conditional]` | `#if` |
|------|----------------|-------|
| CI での Release ビルド | 呼び出し側のコンパイル時にシンボル必要 | Logger 側・アプリ側で独立に制御可能 |
| Release + Logger 有効 | 可能だが呼び出し側の再コンパイルが必要 | `-p:E2ETestEnabled=true` で任意構成で有効化 |
| コードの可読性 | 属性が見えにくい | `#if` ブロックが視覚的に明確 |
| 適用範囲 | void メソッドのみ | 任意のコードブロック |

### 3.2 ビルドシンボル定義

`Directory.Build.props` で以下のように E2E_TEST シンボルを制御する:

```xml
<!-- E2ETest 構成で自動定義（既存） -->
<PropertyGroup Condition="'$(Configuration)' == 'E2ETest'">
  <DefineConstants>$(DefineConstants);E2E_TEST</DefineConstants>
</PropertyGroup>

<!-- MSBuild プロパティによる任意構成での有効化（追加） -->
<PropertyGroup Condition="'$(E2ETestEnabled)' == 'true'">
  <DefineConstants>$(DefineConstants);E2E_TEST</DefineConstants>
</PropertyGroup>
```

### 3.3 ビルドコマンド

```bash
# 開発用（Logger 有効）
dotnet build -c E2ETest

# 本番（Logger 除去）
dotnet build -c Release

# CI（Release 最適化 + Logger 有効）
dotnet build -c Release -p:E2ETestEnabled=true

# CI テスト実行
dotnet test -c Release -p:E2ETestEnabled=true
```

### 3.4 コード上の使い方

```csharp
// TestLogger.cs — Logger ライブラリ側
public static class TestLogger
{
    public static void Attach(LoggerConfig? config = null)
    {
#if E2E_TEST
        // 初期化ロジック
        // E2E_TEST 未定義時はメソッド本体が空になる
#endif
    }
}
```

```csharp
// Program.cs — アプリ側
static void Main()
{
#if E2E_TEST
    TestLogger.Attach();
#endif
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
}
```

---

## 4. ファイル構成

```
src/WinFormsTestHarness.Logger/
├── WinFormsTestHarness.Logger.csproj
├── TestLogger.cs                    # 静的エントリーポイント（#if E2E_TEST で本体をガード）
├── LoggerConfig.cs                  # 設定クラス（デフォルト値付き）
├── Models/
│   ├── LogEntry.cs                  # ログエントリ + ファクトリメソッド（NDJSON 1行 = 1エントリ）
│   └── ControlInfo.cs               # コントロールのメタデータ（Name, FormName, IsPassword等）
├── Internal/
│   ├── PreciseTimestamp.cs          # Stopwatch ベース高精度タイムスタンプ
│   ├── LogPipeline.cs              # ConcurrentQueue + Timer フラッシュ → Sink 書き込み
│   ├── ControlWatcher.cs           # 再帰的コントロールツリー走査 + イベントフック
│   ├── FormTracker.cs              # Application.Idle ポーリングで新フォーム検出
│   └── PasswordDetector.cs         # TextBox.PasswordChar / UseSystemPasswordChar 判定
└── Sinks/
    ├── ILogSink.cs                  # トランスポート抽象インターフェース
    ├── IpcLogSink.cs                # 名前付きパイプクライアント（NDJSON + ハンドシェイク）
    └── JsonFileLogSink.cs           # ローカルファイルフォールバック（NDJSON + ローテーション）
```

### 依存関係グラフ

```
TestLogger
  ├── LoggerConfig
  ├── FormTracker ──► ControlWatcher ──► PasswordDetector
  │                                  ──► ControlInfo
  │                                  ──► LogPipeline
  ├── LogPipeline ──► ILogSink (IpcLogSink / JsonFileLogSink)
  │               ──► LogEntry
  │               ──► PreciseTimestamp
  └── PreciseTimestamp
```

---

## 5. 公開 API

```csharp
namespace WinFormsTestHarness.Logger;

/// <summary>
/// アプリ内ロガーのエントリーポイント。
/// 全メソッドの本体は #if E2E_TEST で囲まれており、
/// E2E_TEST シンボル未定義時は空メソッドとなる。
/// </summary>
public static class TestLogger
{
    private static volatile bool _attached;
    private static LogPipeline? _pipeline;
    private static FormTracker? _formTracker;

    /// <summary>
    /// ロガーを初期化し、フォーム追跡を開始する。
    /// Program.Main() の Application.Run() 前に呼ぶ。
    /// 複数回呼んでも安全（2回目以降は no-op）。
    /// </summary>
    public static void Attach(LoggerConfig? config = null)
    {
#if E2E_TEST
        if (_attached) return;
        try
        {
            config ??= LoggerConfig.Default;
            // IPC Sink + File Sink → Pipeline → FormTracker + ControlWatcher
            _attached = true;
        }
        catch (Exception) { _attached = false; }
#endif
    }

    /// <summary>
    /// 手動でアプリケーションイベントを記録する。
    /// 自動検出できないビジネスロジックイベントの記録に使用。
    /// </summary>
    public static void LogEvent(string controlName, string eventName, object? value = null)
    {
#if E2E_TEST
        try { _pipeline?.Enqueue(LogEntry.EventEntry(...)); } catch { }
#endif
    }

    /// <summary>
    /// 手動でプロパティ変更を記録する。
    /// </summary>
    public static void LogPropertyChanged(string controlName, string propertyName,
        object? oldValue, object? newValue)
    {
#if E2E_TEST
        try { _pipeline?.Enqueue(LogEntry.PropertyChanged(...)); } catch { }
#endif
    }

    /// <summary>
    /// カスタムメッセージを記録する。
    /// </summary>
    public static void Log(string message)
    {
#if E2E_TEST
        try { _pipeline?.Enqueue(LogEntry.Custom(message)); } catch { }
#endif
    }

    /// <summary>
    /// ロガーを終了し、バッファをフラッシュする。
    /// Application.ApplicationExit でも自動呼び出しされる。
    /// </summary>
    public static void Detach()
    {
#if E2E_TEST
        if (!_attached) return;
        try
        {
            _formTracker?.Dispose();
            _pipeline?.Dispose();
            _attached = false;
        }
        catch { }
#endif
    }
}
```

---

## 6. イベントフロー

```
[UI Thread]                          [ThreadPool]                    [Recording Engine]
                                                                      (外部プロセス)
Control.Click ──┐
Control.TextChanged ──┤
Form.FormClosed ──┤
   :              │
   └──► _pipeline.Enqueue(LogEntry)
         │ (ConcurrentQueue, lock-free)
         │
         │              Timer (100ms周期)
         │              ┌──► FlushQueue()
         │              │     while(TryDequeue)
         │              │       │
         │              │       ├── IpcLogSink.Write(entry)  ──► 名前付きパイプ ──► Recording Engine
         │              │       │     ↓ (IOException)
         │              │       └── JsonFileLogSink.Write(entry) ──► ローカルファイル
```

### パイプラインの設計判断

`System.Threading.Timer` + `FlushQueue()` を採用。専用ドレインスレッド（`ManualResetEventSlim` による待機ループ）と比較して:

| 方式 | 利点 | 欠点 |
|------|------|------|
| **Timer (採用)** | シンプル、ThreadPool 再利用、バッチ処理で syscall 削減 | 最大100msのレイテンシ |
| 専用スレッド | エンキュー時即座に wake（低レイテンシ） | スレッド1本追加、実装複雑 |

ロギング用途では100msのレイテンシは十分許容範囲。

---

## 7. LogEntry JSON フォーマット

`recording-integration-design.md` で定義済みの IPC プロトコルに完全準拠する。

### 7.1 メッセージ例

```json
{"ts":"2026-02-22T14:30:05.123456Z","type":"event","control":"btnSearch","event":"Click","form":"SearchForm"}
{"ts":"2026-02-22T14:30:05.234567Z","type":"prop","control":"txtSearchCondition","prop":"Text","old":"","new":"田中","form":"SearchForm"}
{"ts":"2026-02-22T14:30:05.345678Z","type":"prop","control":"txtPassword","prop":"Text","old":"***","new":"****","masked":true,"form":"CustomerEditForm"}
{"ts":"2026-02-22T14:30:06.000000Z","type":"form_open","form":"SearchForm","owner":"MainForm","modal":true}
{"ts":"2026-02-22T14:30:08.000000Z","type":"form_close","form":"SearchForm","result":"OK"}
{"ts":"2026-02-22T14:30:09.000000Z","type":"custom","message":"顧客データの保存が完了"}
```

### 7.2 LogEntry クラス

```csharp
namespace WinFormsTestHarness.Logger.Models;

public sealed class LogEntry
{
    [JsonPropertyName("ts")]      public string Ts { get; set; } = "";
    [JsonPropertyName("type")]    public string Type { get; set; } = "";
    [JsonPropertyName("control")] public string? Control { get; set; }
    [JsonPropertyName("event")]   public string? Event { get; set; }
    [JsonPropertyName("prop")]    public string? Prop { get; set; }
    [JsonPropertyName("old")]     public object? Old { get; set; }
    [JsonPropertyName("new")]     public object? New { get; set; }
    [JsonPropertyName("form")]    public string? Form { get; set; }
    [JsonPropertyName("owner")]   public string? Owner { get; set; }
    [JsonPropertyName("modal")]   public bool? Modal { get; set; }
    [JsonPropertyName("result")]  public string? Result { get; set; }
    [JsonPropertyName("masked")]  public bool? Masked { get; set; }
    [JsonPropertyName("row")]     public int? Row { get; set; }
    [JsonPropertyName("text")]    public string? Text { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}
```

### 7.3 type フィールドの値

| type | 用途 | 主要フィールド |
|------|------|-------------|
| `event` | コントロールイベント（Click, SelectionChanged等） | control, event, form |
| `prop` | プロパティ変更（Text, Checked, SelectedIndex等） | control, prop, old, new, form |
| `form_open` | フォームの表示 | form, owner, modal |
| `form_close` | フォームの閉じ | form, result |
| `custom` | 手動メッセージ | message |
| `sync_response` | 時刻同期ハンドシェイク応答 | clientTs |
| `system` | システムイベント（接続断等） | action |

### 7.4 JSON シリアライズ設定

既存 Inspect プロジェクトの `JsonHelper` と統一:

```csharp
private static readonly JsonSerializerOptions s_jsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};
```

### 7.5 ファクトリメソッド

```csharp
public static class LogEntry
{
    public static LogEntry EventEntry(ControlInfo info, string eventName);
    public static LogEntry PropertyChanged(ControlInfo info, string prop, object? old, object? @new, bool masked = false);
    public static LogEntry FormOpen(string formName, string? ownerName, bool modal);
    public static LogEntry FormClose(string formName, string? dialogResult);
    public static LogEntry Custom(string message);
}
```

### 7.6 値のサニタイズ

```csharp
private static object? Sanitize(object? value)
{
    if (value is null) return null;
    if (value is Delegate) return $"<{value.GetType().Name}>";
    if (value is Type t) return t.FullName;
    var str = value.ToString();
    if (str != null && str.Length > 500)
        return str.Substring(0, 500) + "...(truncated)";
    return value;
}

private static object? MaskValue(object? value)
{
    if (value is null) return null;
    return new string('*', (value.ToString() ?? "").Length);
}
```

---

## 8. IPC 接続ライフサイクル

### 8.1 状態遷移図

```
                    ┌────────────────────────────┐
                    │         INITIAL             │
                    │  (パイプ名未解決 / 環境変数なし) │
                    └────────────┬───────────────┘
                                 │ パイプ名解決
                                 ▼
                    ┌────────────────────────────┐
             ┌─────│       CONNECTING            │
             │     │  _pipe.Connect(timeout)     │
             │     └────────────┬───────────────┘
             │                  │ 成功
             │                  ▼
             │     ┌────────────────────────────┐
             │     │       HANDSHAKE             │
             │     │  sync_request/response/ack  │
             │     └────────────┬───────────────┘
             │                  │ 成功 or ハンドシェイク失敗（非致命的）
             │                  ▼
             │     ┌────────────────────────────┐
             │     │       CONNECTED             │◄─── 再接続成功
             │     │  Write() が正常動作          │
             │     └────────────┬───────────────┘
             │                  │ IOException on Write
             │                  ▼
             │     ┌────────────────────────────┐
      タイム  │     │      DISCONNECTED           │
      アウト  │     │  → ファイルフォールバック     │
             │     └────────────┬───────────────┘
             │                  │ TryReconnect (5秒毎, 最大10回)
             └────►│            ▼
                   ┌────────────────────────────┐
                   │    PERMANENTLY_OFFLINE      │
                   │  ファイルフォールバックのみ    │
                   └────────────────────────────┘
```

### 8.2 パイプ名の解決

```
パイプ名: WinFormsTestHarness_{pid}
  {pid} = Recording Engine のプロセスID

解決順序:
  1. LoggerConfig.RecordingEnginePid（明示指定）
  2. 環境変数 WFTH_RECORDER_PID（Recording Engine が起動時に設定）
  3. どちらもない場合 → IPC 無効、ファイルのみモード
```

### 8.3 時刻同期ハンドシェイク

`recording-integration-design.md` のプロトコルに従う:

```
Recording Engine (Server)              App Logger (Client)
  │                                      │
  ├─ sync_request { serverTs: T1 } ─────►│
  │                                      ├─ clientTs = PreciseTimestamp.Now
  │◄──── sync_response { clientTs: T2 } ─┤
  │                                      │
  ├─ clockOffset 計算                    │
  ├─ sync_ack { offset, rtt } ──────────►│
  │                                      │
  [通常のログ送受信開始]
```

ハンドシェイクに失敗しても非致命的。未補正のタイムスタンプで記録を続行する（同一マシンでのズレは通常 5ms 以内）。

---

## 9. トランスポート層

### 9.1 ILogSink インターフェース

```csharp
namespace WinFormsTestHarness.Logger.Sinks;

internal interface ILogSink
{
    void Write(LogEntry entry);
    bool IsConnected { get; }
}
```

### 9.2 IpcLogSink

```csharp
internal sealed class IpcLogSink : ILogSink, IDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private volatile bool _connected;

    // 接続: _pipe.Connect(timeout) + PerformHandshake()
    // 書き込み: JsonSerializer.Serialize(entry) → _writer.WriteLine(json)
    // 切断検知: IOException → _connected = false → CleanupPipe()
    // 再接続: TryReconnectIfDue() — 5秒間隔、最大10回
}
```

### 9.3 JsonFileLogSink

```csharp
internal sealed class JsonFileLogSink : ILogSink, IDisposable
{
    private readonly string _filePath;  // デフォルト: %TEMP%/WinFormsTestHarness/logs/applog_{datetime}_{pid}.ndjson
    private StreamWriter? _writer;
    private long _currentFileSize;
    private readonly object _writeLock = new();

    // 書き込み: lock内で JsonSerializer.Serialize → _writer.WriteLine
    // ローテーション: _currentFileSize >= _maxFileSize → リネーム → 新規ファイル
    // AutoFlush = true（Timer フラッシュとは独立にファイルバッファをフラッシュ）
}
```

---

## 10. LogPipeline — バックグラウンドキュー

```csharp
internal sealed class LogPipeline : IDisposable
{
    private readonly ConcurrentQueue<LogEntry> _queue = new();
    private readonly ILogSink _primarySink;     // IpcLogSink
    private readonly ILogSink _fallbackSink;    // JsonFileLogSink
    private readonly Timer _flushTimer;         // 100ms 周期
    private readonly int _maxQueueSize;
    private volatile int _queueCount;

    public void Enqueue(LogEntry entry)
    {
        // ConcurrentQueue.Enqueue (lock-free, O(1))
        // キュー溢れ時は古いエントリを自動ドロップ
    }

    private void FlushQueue()
    {
        // Timer コールバック (ThreadPool)
        // while(TryDequeue) → WriteTo(entry)
    }

    private void WriteTo(LogEntry entry)
    {
        // try: _primarySink.Write(entry)
        // catch: _fallbackSink.Write(entry)  ← IPC 失敗時のフォールバック
    }

    public void Dispose()
    {
        // Timer 停止 → 残りキューのフラッシュ → Sink の Dispose
    }
}
```

---

## 11. コントロール自動監視

### 11.1 ControlWatcher

ControlWatcher は Form 上のコントロールツリーを再帰的に走査し、各コントロールの WinForms イベントにハンドラを登録する。

```csharp
internal sealed class ControlWatcher
{
    private readonly LogPipeline _pipeline;
    private readonly int _maxDepth;
    private readonly HashSet<int> _watchedControlIds = new();

    public void WatchRecursive(Control parent, int depth = 0);
    private void WatchControl(Control control);
    private static ControlInfo CreateControlInfo(Control control);
}
```

#### 監視対象イベント

| コントロール型 | 監視イベント | 記録内容 |
|--------------|------------|---------|
| 全コントロール | Click | type="event", event="Click" |
| 全コントロール | GotFocus | type="event", event="GotFocus" |
| 全コントロール | VisibleChanged | type="prop", prop="Visible" |
| 全コントロール | EnabledChanged | type="prop", prop="Enabled" |
| TextBox | TextChanged | type="prop", prop="Text", old/new |
| TextBox | KeyDown (Enter/Tab) | type="event", event="EnterKey"/"TabKey" |
| ComboBox | SelectedIndexChanged | type="prop", prop="SelectedIndex" + text |
| CheckBox | CheckedChanged | type="prop", prop="Checked" |
| RadioButton | CheckedChanged | type="prop", prop="Checked" (true のみ) |
| DataGridView | SelectionChanged | type="event", event="SelectionChanged" + row |
| DataGridView | CellClick | type="event", event="CellClick" + row |
| ListBox | SelectedIndexChanged | type="prop", prop="SelectedIndex" + text |
| NumericUpDown | ValueChanged | type="prop", prop="Value" |
| DateTimePicker | ValueChanged | type="prop", prop="Value" |
| TabControl | SelectedIndexChanged | type="prop", prop="SelectedIndex" + text |

#### 動的コントロール対応

`parent.ControlAdded` イベントをフックし、実行時に追加されるコントロールも自動監視する。

#### コントロール削除時のハンドラ解除

`parent.ControlRemoved` イベントをフックし、削除されたコントロールのイベントハンドラを解除する。これにより、長時間稼働するアプリでの**メモリリークを防止**する。

```csharp
parent.ControlRemoved += (s, e) =>
{
    UnwatchControl(e.Control);
    if (e.Control.HasChildren)
        UnwatchRecursive(e.Control);
};
```

```csharp
private void UnwatchControl(Control control)
{
    var hashCode = control.GetHashCode();
    if (!_watchedControlIds.Remove(hashCode))
        return;  // 監視していないコントロールは無視

    // 登録済みハンドラの解除
    // ※ 匿名ラムダではなく、ControlEventHandlers ディクショナリに保持したデリゲートを使用
    if (_eventHandlers.TryGetValue(hashCode, out var handlers))
    {
        foreach (var (eventName, handler) in handlers)
        {
            switch (eventName)
            {
                case "Click": control.Click -= handler; break;
                case "TextChanged": control.TextChanged -= handler; break;
                case "VisibleChanged": control.VisibleChanged -= handler; break;
                case "EnabledChanged": control.EnabledChanged -= handler; break;
                // コントロール固有イベントも同様に解除
            }
        }
        _eventHandlers.Remove(hashCode);
    }
}
```

**設計上の注意**: `WatchControl` で登録するイベントハンドラは匿名ラムダではなく、`_eventHandlers` ディクショナリに保持する。匿名ラムダでは `-=` による解除ができないため。

#### 名前なしコントロールへの対応

`control.Name` が空の場合、`_{TypeName}_{HashCode:X8}` 形式のフォールバック名を生成する（例: `_Button_0A3F5B2C`）。セッション内では安定した識別子となる。

### 11.2 PasswordDetector

```csharp
internal static class PasswordDetector
{
    public static bool IsPasswordField(Control control)
    {
        if (control is TextBox tb)
        {
            if (tb.PasswordChar != '\0') return true;
            if (tb.UseSystemPasswordChar) return true;
        }
        return false;
    }
}
```

パスワードフィールドと判定された TextBox の TextChanged イベントでは、old/new の値が `***` にマスクされる。

### 11.3 ControlInfo

```csharp
public sealed class ControlInfo
{
    public string Name { get; }            // コントロール名 or フォールバック名
    public string ControlTypeName { get; } // "Button", "TextBox" 等
    public string FormName { get; }        // 親フォームの型名
    public bool IsPasswordField { get; }   // パスワードフィールドか
}
```

UI スレッドで1回だけ生成し、以降はイミュータブルなスナップショットとして使用する。

---

## 12. FormTracker — フォーム開閉検出

```csharp
internal sealed class FormTracker : IDisposable
{
    private readonly LogPipeline _pipeline;
    private readonly ControlWatcher _controlWatcher;
    private readonly HashSet<Form> _trackedForms = new();

    public void Start();   // Application.Idle にフック
    public void Stop();    // Application.Idle からアンフック
}
```

### 検出方式

`Application.Idle` イベントで `Application.OpenForms` をスキャンし、未追跡のフォームを検出する。

| 方式 | 採用 | 理由 |
|------|------|------|
| **Application.Idle ポーリング** | **採用** | 全フォーム種別（モーダル/モードレス）を検出可能。シンプル |
| Application.EnterThreadModal | 不採用 | モーダルフォームのみ。モードレスフォームを検出不可 |
| IMessageFilter | 不採用 | WinForms が "新規トップレベルウィンドウ" メッセージを公開していない |

### フォーム追跡フロー

1. `Application.Idle` 発火 → `ScanOpenForms()`
2. `Application.OpenForms` を列挙、未追跡フォームを検出
3. `LogEntry.FormOpen(...)` をパイプラインにエンキュー
4. `ControlWatcher.WatchRecursive(form)` でフォーム上の全コントロールを監視
5. `form.FormClosed` にハンドラ登録 → `LogEntry.FormClose(...)` をエンキュー

---

## 13. PreciseTimestamp — 高精度タイムスタンプ

```csharp
internal static class PreciseTimestamp
{
    private static readonly long s_baseTimestamp = Stopwatch.GetTimestamp();
    private static readonly DateTimeOffset s_baseTime = DateTimeOffset.UtcNow;
    private static readonly double s_tickFrequency = Stopwatch.Frequency;

    public static DateTimeOffset Now
    {
        get
        {
            var elapsed = Stopwatch.GetTimestamp() - s_baseTimestamp;
            var elapsedMs = elapsed / s_tickFrequency * 1000.0;
            return s_baseTime.AddMilliseconds(elapsedMs);
        }
    }

    public static string NowIso8601 => Now.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
}
```

`Stopwatch.GetTimestamp()` は Windows 上で `QueryPerformanceCounter` ベース（ナノ秒精度）。`DateTimeOffset.UtcNow` の 15.6ms 精度制限を回避する。

Recording Engine にも同一実装を含めることで、同一マシン上のクロック差をほぼゼロにする。

---

## 14. LoggerConfig — 設定

```csharp
public sealed class LoggerConfig
{
    /// <summary>Recording Engine プロセスID。環境変数 WFTH_RECORDER_PID のフォールバックあり。</summary>
    public int? RecordingEnginePid { get; set; }

    /// <summary>パイプ接続タイムアウト（ms）。デフォルト: 3000。</summary>
    public int PipeConnectTimeoutMs { get; set; } = 3000;

    /// <summary>最大キューサイズ。超過時は古いエントリをドロップ。デフォルト: 10000。</summary>
    public int MaxQueueSize { get; set; } = 10_000;

    /// <summary>バッファフラッシュ間隔（ms）。デフォルト: 100。</summary>
    public int FlushIntervalMs { get; set; } = 100;

    /// <summary>フォールバックファイルパス。null 時は %TEMP%/WinFormsTestHarness/logs/ に自動生成。</summary>
    public string? FallbackFilePath { get; set; }

    /// <summary>最大フォールバックファイルサイズ（bytes）。デフォルト: 50MB。</summary>
    public long MaxFallbackFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>IPC 再接続間隔（ms）。デフォルト: 5000。</summary>
    public int ReconnectIntervalMs { get; set; } = 5000;

    /// <summary>最大 IPC 再接続試行回数。デフォルト: 10。</summary>
    public int MaxReconnectAttempts { get; set; } = 10;

    /// <summary>コントロール自動監視の有効/無効。デフォルト: true。</summary>
    public bool AutoWatchControls { get; set; } = true;

    /// <summary>フォーム開閉追跡の有効/無効。デフォルト: true。</summary>
    public bool TrackForms { get; set; } = true;

    /// <summary>再帰的コントロールツリー走査の最大深度。デフォルト: 20。</summary>
    public int MaxControlDepth { get; set; } = 20;

    public static LoggerConfig Default => new();
}
```

---

## 15. スレッド安全性モデル

| コンポーネント | 実行スレッド | 理由 |
|-------------|-----------|------|
| TestLogger.Attach() | UI スレッド (Main) | Program.Main() から呼出 |
| FormTracker.ScanOpenForms() | UI スレッド | Application.Idle は UI スレッドで発火 |
| ControlWatcher.WatchRecursive() | UI スレッド | Control プロパティ参照とイベント購読は UI スレッド必須 |
| イベントハンドラ (Click, TextChanged 等) | UI スレッド | WinForms イベントは作成元スレッドで発火 |
| LogPipeline.Enqueue() | UI スレッド (主) | ConcurrentQueue は lock-free → 任意スレッドから安全 |
| LogPipeline.FlushQueue() | ThreadPool | Timer コールバック |
| IpcLogSink.Write() | ThreadPool | FlushQueue 経由。Timer 周期により事実上シングルスレッド |
| JsonFileLogSink.Write() | ThreadPool | FlushQueue 経由。`_writeLock` でローテーション排他 |

**重要な不変条件**: UI スレッド上のイベントハンドラは `_pipeline.Enqueue(entry)` のみを実行する。これは lock-free の `ConcurrentQueue.Enqueue` であり、UI スレッドのブロック時間は数ナノ秒。シリアライズ、I/O、ネットワーク操作はすべて ThreadPool 上の Timer コールバックで実行する。

---

## 16. .csproj 変更

```xml
<!-- 現在: netstandard2.0 → 変更: net8.0-windows -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
```

`System.Text.Json` は net8.0 に組み込みのため NuGet 追加不要。

---

## 17. SampleApp 統合

### Program.cs

```csharp
#if E2E_TEST
using WinFormsTestHarness.Logger;
#endif

namespace SampleApp;

internal static class Program
{
    [STAThread]
    static void Main()
    {
#if E2E_TEST
        TestLogger.Attach();
#endif
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
```

### SampleApp.csproj

```xml
<!-- Logger は常に参照。#if で呼び出しを制御 -->
<ItemGroup>
  <ProjectReference Include="..\..\src\WinFormsTestHarness.Logger\WinFormsTestHarness.Logger.csproj" />
</ItemGroup>
```

Logger DLL 自体は常にビルドされるが、E2E_TEST 未定義時は全メソッド本体が空になるため実行時コストはゼロ。

---

## 18. 実装順序

| Step | 対象 | 内容 |
|------|------|------|
| 1 | ビルド構成 | Directory.Build.props に `E2ETestEnabled` プロパティ追加 + Logger .csproj を net8.0-windows に変更 |
| 2 | 基盤 | PreciseTimestamp → LogEntry / ControlInfo → LoggerConfig |
| 3 | Sink | ILogSink → JsonFileLogSink → IpcLogSink |
| 4 | パイプライン | LogPipeline |
| 5 | WinForms フック | PasswordDetector → ControlWatcher → FormTracker |
| 6 | エントリーポイント | TestLogger |
| 7 | 統合 | SampleApp の Program.cs / .csproj 変更 |

---

## 19. SampleApp 操作時の期待ログ出力

顧客検索のシナリオ: アプリ起動 → 検索フォーム表示 → "田中" 検索 → 結果選択

```json
{"ts":"...","type":"form_open","form":"MainForm"}
{"ts":"...","type":"event","control":"顧客検索ToolStripMenuItem","event":"Click","form":"MainForm"}
{"ts":"...","type":"form_open","form":"SearchForm","owner":"MainForm","modal":true}
{"ts":"...","type":"prop","control":"txtSearchCondition","prop":"Text","old":"","new":"田","form":"SearchForm"}
{"ts":"...","type":"prop","control":"txtSearchCondition","prop":"Text","old":"田","new":"田中","form":"SearchForm"}
{"ts":"...","type":"event","control":"btnSearch","event":"Click","form":"SearchForm"}
{"ts":"...","type":"event","control":"dgvResults","event":"SelectionChanged","row":0,"form":"SearchForm"}
{"ts":"...","type":"event","control":"btnSelect","event":"Click","form":"SearchForm"}
{"ts":"...","type":"form_close","form":"SearchForm","result":"OK"}
```

文字単位の TextChanged イベントは意図的に粒度を保持している。Recording Engine の `wfth-correlate` がこれを "TextInput" アクションに集約する。

---

## 20. 検証方法

1. `dotnet build -c E2ETest` — ビルド成功（Logger 有効）
2. `dotnet build -c Release` — ビルド成功（Logger メソッド本体が空）
3. `dotnet build -c Release -p:E2ETestEnabled=true` — ビルド成功（Release 最適化 + Logger 有効）
4. SampleApp を E2ETest ビルドで起動 → 操作すると `%TEMP%/WinFormsTestHarness/logs/` に NDJSON 出力
5. パスワードフィールド（CustomerEditForm.txtPassword）の値がマスクされている
6. 単体テスト: LogPipeline、JsonFileLogSink、ControlInfo 生成等
