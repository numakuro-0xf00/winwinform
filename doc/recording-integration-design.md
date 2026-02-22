# Recording Engine 外部連携設計

## 1. アプリ内ロガーとの時刻同期・イベント突合

### 1.1 問題

Recording Engine（外部プロセス）とアプリ内ロガー（対象プロセス内）は別プロセスで動作するため、以下のずれが発生する:

```
wfth-record:  Click記録 (T=14:30:05.123)
アプリ内ロガー: Button.Click イベント発火 (T=14:30:05.128)
                TextChanged イベント発火 (T=14:30:05.135)

問題:
  - 同一マシンでも DateTimeOffset.UtcNow の取得タイミングで数ms〜数十msずれる
  - アプリがビジー状態だとイベント処理の遅延で数百msずれることもある
  - 名前付きパイプの伝送遅延が追加される
```

### 1.2 時刻基準の統一

#### A. クロックソース

両プロセスとも同一マシン上で動作するため、OSクロックは共通。ただしタイムスタンプ取得のタイミングが異なる。

```
方針: 全コンポーネントが同一の高精度タイマーを使用する

推奨クロック: Stopwatch.GetTimestamp() + 基準時刻
  - QueryPerformanceCounter ベース（Windows）
  - ナノ秒精度
  - DateTimeOffset.UtcNow よりも精度が高い（UtcNow は 15.6ms 精度の場合がある）
```

```csharp
/// <summary>
/// 全コンポーネント共通の高精度タイムスタンプ生成器
/// </summary>
public static class PreciseTimestamp
{
    private static readonly long _baseTimestamp = Stopwatch.GetTimestamp();
    private static readonly DateTimeOffset _baseTime = DateTimeOffset.UtcNow;
    private static readonly double _tickFrequency = Stopwatch.Frequency;

    public static DateTimeOffset Now
    {
        get
        {
            var elapsed = Stopwatch.GetTimestamp() - _baseTimestamp;
            var elapsedMs = elapsed / _tickFrequency * 1000.0;
            return _baseTime.AddMilliseconds(elapsedMs);
        }
    }
}
```

アプリ内ロガー（WinFormsTestHarness.Logger）にも同一の実装を含める。同一マシン上で同一アルゴリズムを使えば、クロック差は事実上ゼロになる。

#### B. 基準時刻の同期（クロックスキュー補正）

別プロセスのため `_baseTime` の取得タイミングがずれる。IPC接続時にハンドシェイクで補正する。

```
名前付きパイプ接続時のハンドシェイク:

Recording Engine (Server)              App Logger (Client)
  │                                      │
  ├─ パイプ作成・待受                     │
  │                                      ├─ パイプ接続
  │◄─────────────────────────────────────┤
  │                                      │
  ├─ sync_request { serverTs: T1 } ─────►│
  │                                      ├─ 受信、clientTs = T2 を記録
  │◄──── sync_response { clientTs: T2 } ─┤
  │                                      │
  ├─ RTT = (T3 - T1)                    │
  ├─ oneWayDelay = RTT / 2              │
  ├─ clockOffset = T2 - (T1 + oneWayDelay)
  ├─ 以降、Client のタイムスタンプに      │
  │  clockOffset を加算して補正           │
  │                                      │
  ├─ sync_ack { offset: clockOffset } ──►│
  │                                      │
  [通常のログ送受信開始]
```

```json
// ハンドシェイクメッセージ（IPC上のJSON）
{"type":"sync_request","serverTs":"2026-02-22T14:30:00.000000Z"}
{"type":"sync_response","clientTs":"2026-02-22T14:30:00.003200Z"}
{"type":"sync_ack","offset":1.6,"rtt":3.2,"unit":"ms"}
```

想定されるクロックオフセット: 同一マシンでは 0〜5ms 程度。この補正でサブミリ秒の精度になる。

### 1.3 イベント突合のアルゴリズム

wfth-correlate が入力イベントとアプリ内ロガーのイベントを突合する。

#### A. 時間窓ベースの突合

```
基本ルール:
  入力イベント (T) に対して、T - 50ms 〜 T + correlationWindow ms の
  アプリ内ロガーイベントを候補とする

  - 50ms前: フックコールバック → アプリのイベント処理の順序が逆転する場合の余裕
  - correlationWindow後（デフォルト2000ms）: UIの反応待ち
```

#### B. 因果関係ベースの突合

時間窓だけでは誤突合が起きる。因果関係のヒューリスティクスで精度を上げる。

```
ルール 1 — クリック → コントロールイベント:
  入力: Click on AutomationId="btnSearch"
  アプリログ: { control: "btnSearch", event: "Click" }
  → AutomationId が一致 → 突合確定

ルール 2 — テキスト入力 → TextChanged:
  入力: KeyDown 連続（"田中" を構成するキー列）
  アプリログ: { control: "txtSearch", property: "Text", value: "田中" }
  → フォーカス中のコントロールと一致 → 突合確定

ルール 3 — クリック → フォーム遷移:
  入力: Click on "menuCustomerSearch"
  アプリログ: { type: "FormOpened", form: "SearchForm" }
  → クリック後の最初のFormOpenedイベント → 突合

ルール 4 — 突合不能:
  時間窓内に候補がない、または複数候補で絞れない
  → appLog: [] （空配列）として記録、ログに警告
```

```csharp
class AppLogCorrelator
{
    private readonly TimeSpan _preWindow = TimeSpan.FromMilliseconds(50);
    private readonly TimeSpan _postWindow;

    public IReadOnlyList<AppLogEntry> Correlate(
        CorrelatedAction action,
        IReadOnlyList<AppLogEntry> allAppLogs)
    {
        var candidates = allAppLogs.Where(log =>
            log.Timestamp >= action.Timestamp - _preWindow &&
            log.Timestamp <= action.Timestamp + _postWindow
        ).ToList();

        if (candidates.Count == 0)
            return Array.Empty<AppLogEntry>();

        // 因果関係フィルタ: AutomationId 一致を優先
        var targetId = action.Target?.AutomationId;
        if (!string.IsNullOrEmpty(targetId))
        {
            var matched = candidates
                .Where(c => c.ControlName == targetId)
                .ToList();
            if (matched.Count > 0)
                return matched;
        }

        // フォーム遷移は常に含める
        var formEvents = candidates
            .Where(c => c.Type is "FormOpened" or "FormClosed")
            .ToList();

        // 時間的に最も近いイベントを含める
        var nearest = candidates
            .OrderBy(c => Math.Abs(
                (c.Timestamp - action.Timestamp).TotalMilliseconds))
            .Take(3)
            .ToList();

        return formEvents.Union(nearest).OrderBy(c => c.Timestamp).ToList();
    }
}
```

### 1.4 IPC プロトコル詳細

#### メッセージフォーマット

名前付きパイプ上のプロトコル。各メッセージは改行区切りのJSON（NDJSON）。

```
パイプ名: WinFormsTestHarness_{pid}_{sessionNonce}
  {pid} は Recording Engine のプロセスID
  {sessionNonce} は Recording Engine 起動時に生成する128bit乱数（hex）
  → 複数セッション同時実行を許容
```

#### 接続保護（必須）

```
1. ACL 制限:
   - NamedPipeServerStream 作成時に PipeSecurity を設定し、
     同一ユーザー SID のみ接続許可
   - Administrators / Everyone への広域許可はしない

2. 初期ハンドシェイク:
   - 接続直後に hello/challenge/response を実施
   - sessionNonce と別に共有された sessionToken（起動時配布）を検証
   - 検証失敗時は即切断し、securityイベントを記録

3. 監査ログ:
   - 認証失敗・ACL拒否・再接続を system イベントとして NDJSON 出力
```

```json
{"ts":"...","type":"hello","pid":12345,"sessionNonce":"a1b2..."}
{"ts":"...","type":"challenge","nonce":"9f10..."}
{"ts":"...","type":"response","proof":"hmac-sha256(...)"}
{"ts":"...","type":"system","action":"ipc_auth_failed","reason":"invalid_proof"}
```

```json
// アプリ内ロガー → Recording Engine

// コントロールイベント
{"ts":"...","type":"event","control":"btnSearch","event":"Click","form":"MainForm"}

// プロパティ変更
{"ts":"...","type":"prop","control":"txtSearch","prop":"Text","old":"","new":"田中","form":"SearchForm"}

// フォーム開閉
{"ts":"...","type":"form_open","form":"SearchForm","owner":"MainForm","modal":true}
{"ts":"...","type":"form_close","form":"SearchForm","result":"OK"}

// チェックボックス/コンボボックス
{"ts":"...","type":"prop","control":"chkPartialMatch","prop":"Checked","old":true,"new":false}
{"ts":"...","type":"prop","control":"cmbCategory","prop":"SelectedIndex","old":0,"new":1,"text":"法人"}

// DataGridView選択
{"ts":"...","type":"event","control":"dgvResults","event":"SelectionChanged","row":2,"form":"SearchForm"}

// パスワードフィールド（値をマスク）
{"ts":"...","type":"prop","control":"txtPassword","prop":"Text","old":"***","new":"****","masked":true}
```

#### パスワードフィールドのマスキング

```csharp
// アプリ内ロガー側の判定
private bool ShouldMask(Control control)
{
    // TextBox.PasswordChar が設定されている
    if (control is TextBox tb && tb.PasswordChar != '\0')
        return true;
    // TextBox.UseSystemPasswordChar が true
    if (control is TextBox tb2 && tb2.UseSystemPasswordChar)
        return true;
    return false;
}

private string MaskValue(string value)
{
    return new string('*', value.Length);
}
```

#### 接続断の処理

```
接続断の検知:
  - パイプの Write/Read が IOException をスロー
  - パイプの IsConnected が false

対応:
  Recording Engine 側:
    - 警告ログを stderr に出力
    - NDJSON に接続断イベントを記録
      {"ts":"...","type":"system","action":"ipc_disconnected"}
    - 記録自体は継続（外部観察のみで動作）
    - 定期的に再接続を試行（5秒間隔、最大10回）

  アプリ内ロガー側:
    - ローカルファイルにフォールバック出力
    - 再接続成功時にバッファを送信
```

### 1.5 突合精度の評価指標

wfth-correlate の出力に突合の品質メトリクスを含める。  
形式はモノリシック JSON ではなく、**NDJSON のメタ行**として出力する。
メタ行の共通契約は `recording-cli-design.md` の「3.6 統合ログ出力形式」を参照。

```json
{"seq":1,"type":"Click", ...}
{"seq":2,"type":"TextInput", ...}
{"type":"summary","summaryType":"correlation","metrics":{"totalActions":25,"withAppLog":20,"withoutAppLog":5,"avgClockOffset":2.3,"maxClockOffset":8.1,"unit":"ms","ipcStatus":"connected","ipcDisconnections":0}}
```

---

## 2. CIでのヘッドレス実行

### 2.1 問題

CI/CDパイプラインでE2Eテストを実行するには、GUIを持つWinFormsアプリを「ヘッドレス」で動かす必要がある。しかしWinFormsはGDI+ベースでディスプレイに依存しており、完全なヘッドレス実行は困難。

### 2.2 Windows CI環境の実態

| 環境 | GUI | UIA | フック | スクリーンショット |
|------|-----|-----|--------|-------------------|
| GitHub Actions (windows-latest) | 仮想デスクトップあり | 動作する | 動作する | 撮影可能 |
| Azure DevOps (Windows Agent) | インタラクティブセッション設定可 | 設定次第 | 設定次第 | 設定次第 |
| Jenkins (Windows Service) | デフォルトでSession 0（GUIなし） | 動作しない | 動作しない | 不可 |
| ローカルCI (自前Windows VM) | 設定次第 | 設定次第 | 設定次第 | 設定次第 |

**重要な発見: GitHub Actions の `windows-latest` は仮想デスクトップを持つ。** GUIアプリの起動、UIA操作、スクリーンショット撮影が可能。

### 2.3 CI実行モード

```
2つの実行モード:

  A. Recording再生モード（主要ユースケース）
     - 事前にRecordingした操作ログを元にテストを再実行
     - wfth-record は不要（入力フック不要）
     - WinFormsTestHarness.Core がテストを実行
     - CI環境で最も一般的

  B. Recording記録モード（特殊）
     - CI上でRecordingを実行（通常は手動で行う）
     - 仮想デスクトップが必要
     - 限定的なユースケース
```

本設計では **A. 再生モード** のCI対応を主眼とし、Recording記録はローカルで行う前提とする。

### 2.4 CI環境の要件

#### A. 必須要件

```
1. インタラクティブなWindowsセッション
   - Session 0 (サービスセッション) ではGUIアプリが起動できない
   - Session 1+ (インタラクティブセッション) が必要

2. 仮想デスクトップの解像度
   - 最低 1024x768
   - 推奨 1920x1080
   - GitHub Actions: デフォルト 1024x768

3. .NET 8 SDK
   - テスト実行に必要

4. ディスプレイアダプター
   - GDI+ が動作すること
   - GitHub Actions: ソフトウェアレンダリングで動作
```

#### B. GitHub Actions の設定例

```yaml
name: E2E Tests

on: [push, pull_request]

jobs:
  e2e-test:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Build (E2ETest config)
        run: dotnet build -c E2ETest

      - name: Set screen resolution
        run: |
          # PowerShell で解像度を設定（GitHub Actions では制限あり）
          # 代替: テスト側でウィンドウサイズを固定する
          echo "Using default resolution"

      - name: Run E2E tests
        run: dotnet test -c E2ETest --logger "trx;LogFileName=results.trx"
        env:
          E2E_TARGET_APP: ${{ github.workspace }}\samples\SampleApp\bin\E2ETest\net8.0-windows\SampleApp.exe

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: |
            **/*.trx
            **/screenshots/**
```

### 2.5 CI固有の課題と対策

#### A. タイミング問題

```
問題: CI環境は物理マシンより遅い。UIの描画、イベント処理に時間がかかる。

対策:
  - テストフレームワーク(Core)のデフォルトタイムアウトをCI用に調整
    ローカル: 5秒
    CI:      15秒
  - 環境変数 E2E_TIMEOUT_MULTIPLIER で倍率指定
  - ポーリング間隔もCI用に調整（100ms → 200ms）
```

```csharp
public static class TestConfig
{
    public static TimeSpan DefaultTimeout =>
        TimeSpan.FromSeconds(5) * TimeoutMultiplier;

    public static double TimeoutMultiplier =>
        double.TryParse(
            Environment.GetEnvironmentVariable("E2E_TIMEOUT_MULTIPLIER"),
            out var m) ? m : 1.0;
}
```

#### B. 画面解像度の固定

```
問題: CI環境の解像度が異なると、座標ベースの操作やスクリーンショット比較が不安定。

対策:
  - テスト開始時にアプリウィンドウのサイズと位置を固定する
  - 解像度に依存しないUIAベースの操作を優先
  - 座標ベースの操作は相対座標を使用
```

```csharp
public class AppInstance
{
    public async Task NormalizeWindow()
    {
        // CI環境でのウィンドウ位置・サイズを固定
        NativeMethods.MoveWindow(_mainHwnd,
            x: 0, y: 0,
            width: 1024, height: 768,
            repaint: true);
    }
}
```

#### C. テスト間の分離

```
問題: テストが前のテストの状態を引き継ぐ

対策:
  - テストごとにアプリを再起動（推奨）
  - または、テスト前に既知の状態にリセットする機構を提供
  - サンプルデータをテストごとに初期化
```

#### D. スクリーンショットの期待値比較

```
問題: CI環境のフォントレンダリングやアンチエイリアスがローカルと異なる

対策:
  - ピクセル完全一致ではなく、類似度ベースの比較
  - 閾値: 95% 一致で合格
  - CI用のリファレンス画像セットを別途管理
  - または、スクリーンショット比較はCIでは無効にし、
    UIA ベースのアサーションのみを実行
```

### 2.6 CI向け wfth ツールの使い方

CI環境ではRecordingは行わないが、テスト実行時に wfth-inspect を診断ツールとして使える。

```yaml
      - name: Run E2E tests with diagnostics
        run: |
          # テスト対象アプリを起動
          Start-Process -FilePath $env:E2E_TARGET_APP
          Start-Sleep -Seconds 3

          # UIAツリーをダンプ（デバッグ用）
          dotnet run --project src/WinFormsTestHarness.Inspect -- tree --process SampleApp > uia-tree.json

          # テスト実行
          dotnet test -c E2ETest

          # テスト後のUIAツリーもダンプ
          dotnet run --project src/WinFormsTestHarness.Inspect -- tree --process SampleApp > uia-tree-after.json
        shell: pwsh

      - name: Upload diagnostics
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: diagnostics
          path: |
            uia-tree*.json
            **/screenshots/**
```

### 2.7 Azure DevOps の場合

```yaml
# azure-pipelines.yml
pool:
  vmImage: 'windows-latest'

steps:
  - task: UseDotNet@2
    inputs:
      version: '8.0.x'

  - script: dotnet build -c E2ETest
    displayName: 'Build'

  - script: dotnet test -c E2ETest --logger trx
    displayName: 'E2E Tests'
    env:
      E2E_TIMEOUT_MULTIPLIER: '2.0'

  - task: PublishTestResults@2
    condition: always()
    inputs:
      testResultsFormat: 'VSTest'
      testResultsFiles: '**/*.trx'
```

### 2.8 Session 0 問題の回避

Jenkins等のWindowsサービスとして動作するCI環境では、Session 0で実行されるためGUIアプリが起動できない。

```
回避策:
  A. エージェントをインタラクティブセッションで起動する
     - 自動ログオン設定 + スタートアップにエージェントを登録
     - 最も確実だがセキュリティ上の考慮が必要

  B. PsExec でセッション切り替え
     - PsExec -i -s <command> でインタラクティブセッションで実行
     - 管理者権限が必要

  C. 仮想マシンを使う
     - Hyper-V / VirtualBox の VM をCI用に用意
     - RDP接続でインタラクティブセッションを確保
     - 最も柔軟だがインフラコストがかかる
```

### 2.9 実装優先度

| 機能 | 優先度 | 理由 |
|------|--------|------|
| GitHub Actions での基本動作 | **MVP E** | Core フレームワークと同時に |
| E2E_TIMEOUT_MULTIPLIER | **MVP E** | CI安定性の基本 |
| ウィンドウサイズ固定 | **MVP E** | 再現性の基本 |
| テストごとのアプリ再起動 | **MVP E** | 分離の基本 |
| Azure DevOps 対応 | 将来 | GitHub Actions で十分開始できる |
| Session 0 回避 | 将来 | Jenkins 利用時のみ必要 |
| CI用リファレンス画像管理 | 将来 | 画像認識実装後 |
