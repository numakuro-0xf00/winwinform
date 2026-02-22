# WinForms E2E テスト自動化プラットフォーム — 設計ドキュメント

> 更新注記（2026-02-22）: Recording パイプラインは `wfth-aggregate` + `wfth-correlate` 分割を正とする。

## 1. プロジェクト概要

### 背景と課題

Webアプリケーション開発ではPlaywright等によるE2Eテスト自動化が普及しているが、Windowsデスクトップアプリケーション（特にWinForms）の開発現場では、UIテストはほとんど手作業で行われている。

既存ツール（Coded UI廃止済、Appium不安定、WinAppDriverメンテナンス停滞、商用ツールは高コスト）では現場の課題を解決できていない。

### ビジョン

**ハイブリッドアプローチ**（UI Automation + 画像認識 + AIエージェント向け専用ロガー）を組み合わせ、WinFormsレガシーアプリのE2Eテストを自動化するプラットフォームを構築する。

### ターゲット

- **主要対象**: WinForms（Windows Forms）で構築されたレガシーアプリケーション
- WinFormsは標準コントロールのUI Automation対応が比較的良好だが、サードパーティコントロール（DevExpress、Infragistics等）やカスタム描画コントロールがUIAで認識できない問題がある

### 実現フェーズ

```
Phase 1: Recording & 回帰テスト生成
  - 手動テスト操作をRecordingし、AIエージェントが回帰テストコードを自動生成
  - テスト仕様書をメタデータとして活用

Phase 2: 仕様書駆動テスト生成（将来構想）
  - テスト仕様書からRecordingなしで直接テストコードを生成
```

---

## 2. アーキテクチャ全体像

```
テスト仕様書(Excel/Word等)
     │
     ▼
┌──────────┐
│ wfth-parse│───→ test-spec.json
└──────────┘

wfth-record --capture ──→ record.ndjson
wfth-inspect watch  ───→ uia.ndjson
アプリ内ロガー(IPC)  ──→ app-log.ndjson
           │
           ▼
wfth-aggregate < record.ndjson
           │
           ▼
wfth-correlate --uia uia.ndjson --screenshots <dir> [--app-log ...]
           │
           ▼
session.ndjson（標準出力）
           │
           ├─→ AIエージェント（NDJSONを直接入力）
           └─→ jq -s / wfth-session（将来）→ session.json

（将来）wfth-correlate --spec test-spec.json で仕様書ステップ突合
```

---

## 3. テストコード出力形式（3層構造）

### 設計原則

- 既存のテストランナー（NUnit / xUnit / MSTest）と統合可能
- C#で出力（WinFormsアプリとの親和性）
- UI操作の抽象化レイヤーで UIA と画像認識の切り替えを隠蔽

### レイヤー構成

```
┌──────────────────────────────────────────────┐
│  テストケース層（AIが生成するコード）            │
│  NUnit/xUnit/MSTest 互換                      │
│  テスト仕様書の「意図」をそのまま表現           │
├──────────────────────────────────────────────┤
│  操作抽象化層（フレームワーク本体）             │
│  Page Object パターンで画面を抽象化            │
│  UIA / 画像認識 / フォールバックを内部で制御    │
├──────────────────────────────────────────────┤
│  ドライバー層                                  │
│  UIAutomationDriver / ImageRecognitionDriver  │
│  実際のOS・アプリとのやり取り                   │
└──────────────────────────────────────────────┘
```

### 3.1 テストケース層（AIが生成）

```csharp
[TestFixture]
public class CustomerSearchTest : WinFormsTestBase
{
    [Test]
    [TestCaseSource("TC-001")]
    public async Task 顧客検索_田中で検索_1件表示される()
    {
        // Arrange
        var mainForm = await App.GetForm<MainFormPage>();

        // Act
        await mainForm.CustomerSearchButton.ClickAsync();

        var searchForm = await App.WaitForForm<SearchFormPage>();
        await searchForm.SearchCondition.SetTextAsync("田中");
        await searchForm.SearchButton.ClickAsync();

        // Assert
        await searchForm.SearchResultGrid
            .Should().ContainText("田中太郎");
        await searchForm.ResultCount
            .Should().HaveText("1件");
    }
}
```

**設計意図**: テスト仕様書の日本語がそのまま読み取れるレベルの可読性。座標やAutomationIdといった実装詳細が一切露出しない。

### 3.2 操作抽象化層（Page Objectパターン）

```csharp
public class SearchFormPage : FormPage
{
    // 要素の特定方法を優先順位付きで定義
    public IElement SearchCondition => Element(
        Strategy.ByAutomationId("txtSearchCondition"),    // 第1候補: UIA
        Strategy.ByName("検索条件"),                        // 第2候補: UIA Name
        Strategy.ByImage("search_textbox.png"),             // 第3候補: 画像認識
        Strategy.ByRelativePosition("検索ボタンの左隣のテキストボックス") // 最終手段: AI判断
    );

    public IElement SearchButton => Element(
        Strategy.ByAutomationId("btnSearch"),
        Strategy.ByName("検索"),
        Strategy.ByImage("search_button.png")
    );

    public IElement SearchResultGrid => Element(
        Strategy.ByAutomationId("dgvResults"),
        Strategy.ByControlType(ControlType.DataGrid)
    );

    public IElement ResultCount => Element(
        Strategy.ByAutomationId("lblCount"),
        Strategy.ByPattern(@"\d+件")  // OCR + 正規表現
    );
}
```

### 3.3 ドライバー層（ハイブリッドフォールバック機構）

```csharp
public class HybridElementLocator : IElementLocator
{
    public async Task<IElement> FindAsync(params Strategy[] strategies)
    {
        foreach (var strategy in strategies)
        {
            try
            {
                var element = strategy switch
                {
                    UIAStrategy uia => await _uiaDriver.FindAsync(uia),
                    ImageStrategy img => await _imageDriver.FindAsync(img),
                    RelativeStrategy rel => await _aiDriver.FindAsync(rel),
                    _ => null
                };

                if (element != null)
                {
                    Log.Info($"要素特定成功: {strategy.Description}");
                    return element;
                }
            }
            catch (ElementNotFoundException)
            {
                Log.Warn($"要素特定失敗、次の戦略へ: {strategy.Description}");
                continue;
            }
        }

        // 全戦略失敗 → スクリーンショット付きで詳細レポート
        throw new AllStrategiesFailedException(strategies,
            await CaptureCurrentState());
    }
}
```

### 3.4 テストランナー統合

```xml
<!-- .csproj -->
<PackageReference Include="NUnit" Version="4.*" />
<PackageReference Include="WinFormsTestHarness" Version="1.0.0" />
```

```csharp
public abstract class WinFormsTestBase
{
    protected AppInstance App { get; private set; }

    [OneTimeSetUp]
    public async Task LaunchApp()
    {
        App = await AppInstance.LaunchAsync(new LaunchConfig
        {
            ExePath = TestConfig.TargetAppPath,
            AttachLogger = true,            // アプリ内ロガー有効化
            ScreenshotOnFailure = true,
            DefaultTimeout = TimeSpan.FromSeconds(10)
        });
    }

    [TearDown]
    public async Task CaptureResultOnFailure()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            await App.CaptureFailureReport();  // スクショ+UIツリー+ログを保存
        }
    }

    [OneTimeTearDown]
    public async Task CloseApp() => await App.CloseAsync();
}
```

`dotnet test` で実行可能。CI/CDパイプラインに組み込める。

### 3.5 AI生成ワークフロー

```
Recording時のログ + テスト仕様書
    ↓
AIエージェント
    ↓
生成物:
  ├── TestCase.cs           （テストケース層）
  ├── XxxFormPage.cs        （Page Object層）
  └── reference_images/     （画像認識用リファレンス画像）
    ↓
人間がレビュー・微調整
    ↓
回帰テストスイートに追加
```

---

## 4. アプリ内ロガー（インストルメンテーション）

### 設計原則

- NuGetパッケージとして提供
- 既存アプリへの変更は最小限（Program.csに `#if E2E_TEST` ブロックを1箇所追加）
- **`#if E2E_TEST` プリプロセッサディレクティブにより、シンボル未定義ビルドではロガーのコードが IL に含まれない**

### 4.1 条件付きコンパイルによる本番ビルド無害化

`#if E2E_TEST` / `#endif` プリプロセッサディレクティブを採用する。`[Conditional("E2E_TEST")]` 属性ではなく `#if` を選択した理由:

- CI で `dotnet build -c Release -p:E2ETestEnabled=true`（Release 最適化 + Logger 有効）が可能
- Logger 側・アプリ側で独立にシンボル制御できる
- `#if` ブロックが視覚的に明確で、void メソッド以外にも適用可能

詳細は `logger-architecture-design.md` セクション3を参照。

```csharp
public static class TestLogger
{
    public static void Attach(LoggerConfig? config = null)
    {
#if E2E_TEST
        // テストビルド時のみ実行
        // E2E_TEST 未定義時はメソッド本体が空になる
#endif
    }

    public static void LogEvent(string controlName, string eventName, object? value = null)
    {
#if E2E_TEST
        // 同様
#endif
    }
}
```

```csharp
// Program.cs — #if E2E_TEST ブロックを追加
static void Main()
{
#if E2E_TEST
    TestLogger.Attach();
#endif
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
}
```

### ビルド構成

```xml
<!-- Directory.Build.props -->
<!-- E2ETest 構成で自動定義 -->
<PropertyGroup Condition="'$(Configuration)' == 'E2ETest'">
    <DefineConstants>$(DefineConstants);E2E_TEST</DefineConstants>
</PropertyGroup>

<!-- MSBuild プロパティによる任意構成での有効化 -->
<PropertyGroup Condition="'$(E2ETestEnabled)' == 'true'">
    <DefineConstants>$(DefineConstants);E2E_TEST</DefineConstants>
</PropertyGroup>
```

```bash
dotnet build -c Release                          # 本番バイナリ（ロガーメソッド本体が空）
dotnet build -c E2ETest                          # ロガー有効、テスト用バイナリ
dotnet build -c Release -p:E2ETestEnabled=true   # CI用: Release最適化 + ロガー有効
```

### 4.2 ロガーライブラリ構成

```
WinFormsTestHarness.Logger (NuGetパッケージ)
├── TestLogger.cs           … エントリーポイント
├── ControlWatcher.cs       … コントロール監視
├── EventInterceptor.cs     … イベント傍受
├── StateSnapshot.cs        … 状態スナップショット
└── LogSink/
    ├── ILogSink.cs
    ├── JsonFileLogSink.cs  … ファイル出力
    └── IpcLogSink.cs       … 外部プロセス連携（名前付きパイプ）
```

### 4.3 コントロール自動監視

```csharp
public static class TestLogger
{
    private static ControlWatcher _watcher;
    private static ILogSink _sink;

    [Conditional("E2E_TEST")]
    public static void Attach(LoggerConfig config = null)
    {
        config ??= LoggerConfig.Default;
        _sink = config.CreateSink();

        Application.Idle += OnIdle;
        FormTracker.FormOpened += OnFormOpened;
        FormTracker.Start();
    }

    private static void OnFormOpened(object sender, Form form)
    {
        _watcher = new ControlWatcher(_sink);
        _watcher.WatchRecursive(form);
    }
}
```

```csharp
internal class ControlWatcher
{
    private readonly ILogSink _sink;

    public void WatchRecursive(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            WatchControl(control);
            if (control.HasChildren)
                WatchRecursive(control);
        }

        // 動的に追加されるコントロールも監視
        parent.ControlAdded += (s, e) =>
        {
            WatchControl(e.Control);
            if (e.Control.HasChildren)
                WatchRecursive(e.Control);
        };
    }

    private void WatchControl(Control control)
    {
        var info = new ControlInfo(control);  // Name, Type, 親フォーム等

        // 共通イベント
        control.Click += (s, e) =>
            _sink.Write(LogEntry.Event(info, "Click", e));
        control.TextChanged += (s, e) =>
            _sink.Write(LogEntry.PropertyChanged(info, "Text", control.Text));
        control.VisibleChanged += (s, e) =>
            _sink.Write(LogEntry.PropertyChanged(info, "Visible", control.Visible));
        control.EnabledChanged += (s, e) =>
            _sink.Write(LogEntry.PropertyChanged(info, "Enabled", control.Enabled));

        // コントロール固有イベント
        switch (control)
        {
            case ComboBox cb:
                cb.SelectedIndexChanged += (s, e) =>
                    _sink.Write(LogEntry.PropertyChanged(info, "SelectedIndex", cb.SelectedIndex));
                break;
            case DataGridView dgv:
                dgv.SelectionChanged += (s, e) =>
                    _sink.Write(LogEntry.Event(info, "SelectionChanged",
                        new { RowIndex = dgv.CurrentRow?.Index }));
                break;
            case CheckBox chk:
                chk.CheckedChanged += (s, e) =>
                    _sink.Write(LogEntry.PropertyChanged(info, "Checked", chk.Checked));
                break;
            // 必要に応じて拡張
        }
    }
}
```

### 4.4 導入ステップ

```
Step 1: NuGetパッケージ追加（E2ETest構成のみ）
Step 2: Program.cs に TestLogger.Attach() を1行追加
Step 3: ビルド構成に E2ETest を追加
→ 既存コードの変更は実質2箇所のみ
→ 本番バイナリへの影響はゼロ
```

---

## 5. 外部観察エンジン（Recording Engine）

### 5.1 全体構成

```
┌──────────────────────────────────────────────────────────┐
│ wfth-record   │ 入力フック + （任意）スクリーンショット撮影 │
│               │ 出力: record.ndjson + screenshots/*.png    │
└──────────────────────────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────┐
│ wfth-aggregate │ 生イベントを操作アクションに集約          │
│                │ 出力: aggregated-action NDJSON (stdout)  │
└──────────────────────────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────┐
│ wfth-correlate │ 時間窓相関 + ノイズ分類 + 仕様突合         │
│                │ 入力: stdin + uia/app-log/screenshots    │
│                │ 出力: session.ndjson                     │
└──────────────────────────────────────────────────────────┘
                           ▲
                           │
┌──────────────────────────────────────────────────────────┐
│ wfth-inspect watch │ UIAツリー変化監視（uia.ndjson）       │
└──────────────────────────────────────────────────────────┘
```

### 5.2 Input Hook Manager

対象アプリのウィンドウに対する入力のみをキャプチャする。`SetWindowsHookEx` でグローバルフックを設定し、フォアグラウンドウィンドウが対象アプリ（または子ウィンドウ）かどうかを判定してフィルタリングする。

```csharp
public class InputHookManager : IDisposable
{
    private readonly IntPtr _mouseHookId;
    private readonly IntPtr _keyboardHookId;
    private readonly IntPtr _targetWindowHandle;

    public event EventHandler<MouseEventRecord> MouseAction;
    public event EventHandler<KeyboardEventRecord> KeyboardAction;

    public InputHookManager(IntPtr targetWindowHandle)
    {
        _targetWindowHandle = targetWindowHandle;

        _mouseHookId = NativeMethods.SetWindowsHookEx(
            HookType.WH_MOUSE_LL, MouseHookCallback, IntPtr.Zero, 0);
        _keyboardHookId = NativeMethods.SetWindowsHookEx(
            HookType.WH_KEYBOARD_LL, KeyboardHookCallback, IntPtr.Zero, 0);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsTargetWindow())
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var action = (MouseMessage)wParam;

            if (IsSignificantAction(action))  // Move以外、またはドラッグ中のMove
            {
                MouseAction?.Invoke(this, new MouseEventRecord
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Action = action,
                    ScreenX = info.pt.x,
                    ScreenY = info.pt.y,
                    RelativeX = info.pt.x - _windowRect.Left,
                    RelativeY = info.pt.y - _windowRect.Top
                });
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsTargetWindow())
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var action = (KeyboardMessage)wParam;

            KeyboardAction?.Invoke(this, new KeyboardEventRecord
            {
                Timestamp = DateTimeOffset.UtcNow,
                Action = action,
                VirtualKeyCode = info.vkCode,
                KeyName = KeyInterop.KeyFromVirtualKey(info.vkCode).ToString(),
                IsModifier = IsModifierKey(info.vkCode)
            });
        }
        return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private bool IsTargetWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        return foreground == _targetWindowHandle
            || NativeMethods.IsChild(_targetWindowHandle, foreground)
            || GetRootOwner(foreground) == _targetWindowHandle;
    }
}
```

### キーボード入力のシーケンス集約（wfth-aggregate）

`wfth-record` は生キーイベントを保存し、`wfth-aggregate` が文字列入力へ集約する。

```csharp
public class KeyboardSequenceAggregator
{
    private readonly StringBuilder _buffer = new();
    private DateTimeOffset _lastKeyTime;
    private const int SequenceTimeoutMs = 500;

    public event EventHandler<TextInputRecord> TextInputCompleted;

    public void OnKeyEvent(KeyboardEventRecord e)
    {
        if (e.Action != KeyboardMessage.WM_KEYDOWN) return;

        var elapsed = e.Timestamp - _lastKeyTime;
        if (elapsed.TotalMilliseconds > SequenceTimeoutMs && _buffer.Length > 0)
            FlushBuffer();

        if (IsSpecialKey(e.VirtualKeyCode))
        {
            FlushBuffer();
            TextInputCompleted?.Invoke(this, new TextInputRecord
            {
                Type = TextInputType.SpecialKey,
                Value = e.KeyName,
                Timestamp = e.Timestamp
            });
        }
        else if (IsPrintableChar(e.VirtualKeyCode))
        {
            _buffer.Append(ToChar(e));
            _lastKeyTime = e.Timestamp;
        }
    }

    private void FlushBuffer()
    {
        if (_buffer.Length == 0) return;
        TextInputCompleted?.Invoke(this, new TextInputRecord
        {
            Type = TextInputType.Text,
            Value = _buffer.ToString(),
            Timestamp = _lastKeyTime
        });
        _buffer.Clear();
    }
}
```

### 5.3 Screen Capturer

対象ウィンドウ領域のみをキャプチャ。前回との差分検知により無意味なスクリーンショットを省く。

```csharp
public class ScreenCapturer
{
    private readonly IntPtr _targetWindowHandle;
    private Bitmap _previousCapture;

    public ScreenshotRecord Capture(string triggeredBy)
    {
        var rect = GetWindowRect(_targetWindowHandle);
        var bitmap = new Bitmap(rect.Width, rect.Height);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0,
                new Size(rect.Width, rect.Height));
        }

        var record = new ScreenshotRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            TriggeredBy = triggeredBy,
            WindowRect = rect,
            Image = bitmap,
            HasVisualChange = DetectVisualChange(bitmap)
        };

        _previousCapture?.Dispose();
        _previousCapture = (Bitmap)bitmap.Clone();
        return record;
    }

    private bool DetectVisualChange(Bitmap current)
    {
        if (_previousCapture == null) return true;

        // 高速比較: リサイズしたサムネイルでピクセル差分を計算
        var prevThumb = ResizeForComparison(_previousCapture, 64, 48);
        var currThumb = ResizeForComparison(current, 64, 48);
        var diffRatio = CalculatePixelDiffRatio(prevThumb, currThumb);
        return diffRatio > 0.02;  // 2%以上の変化で有意とみなす
    }
}
```

### キャプチャタイミング制御

```csharp
public class CaptureStrategy
{
    private readonly ScreenCapturer _capturer;
    private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(200);
    private DateTimeOffset _lastCapture = DateTimeOffset.MinValue;

    public async Task<ScreenshotRecord> CaptureOnInputEvent(string eventDescription)
    {
        var before = _capturer.Capture($"before:{eventDescription}");
        await Task.Delay(300);  // UIの反応を待つ
        var after = _capturer.Capture($"after:{eventDescription}");

        if (after.HasVisualChange)
            return new CompositeScreenshot(before, after);
        else
            return after;
    }

    public async Task MonitorForTransitions(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(500, ct);
            var capture = _capturer.Capture("periodic_check");
            if (capture.HasVisualChange)
                OnScreenTransitionDetected?.Invoke(this, capture);
        }
    }
}
```

### 5.4 UIA Tree Snapshotter（`wfth-inspect watch` 相当）

```csharp
public class UIATreeSnapshotter
{
    private readonly AutomationElement _rootElement;
    private UIATreeNode _previousSnapshot;

    public UIATreeSnapshotter(IntPtr windowHandle)
    {
        _rootElement = AutomationElement.FromHandle(windowHandle);
    }

    public UIASnapshotRecord TakeSnapshot(string triggeredBy)
    {
        var currentTree = WalkTree(_rootElement, maxDepth: 10);
        var diff = _previousSnapshot != null
            ? ComputeDiff(_previousSnapshot, currentTree)
            : null;
        _previousSnapshot = currentTree;

        return new UIASnapshotRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            TriggeredBy = triggeredBy,
            Tree = currentTree,
            Diff = diff
        };
    }

    private UIATreeNode WalkTree(AutomationElement element, int maxDepth, int depth = 0)
    {
        if (depth >= maxDepth) return null;

        var node = new UIATreeNode
        {
            AutomationId = element.Current.AutomationId,
            Name = element.Current.Name,
            ControlType = element.Current.ControlType.ProgrammaticName,
            ClassName = element.Current.ClassName,
            BoundingRect = element.Current.BoundingRectangle,
            IsEnabled = element.Current.IsEnabled,
            IsOffscreen = element.Current.IsOffscreen,
            Value = TryGetValue(element),
            Children = new List<UIATreeNode>()
        };

        var walker = TreeWalker.ControlViewWalker;
        var child = walker.GetFirstChild(element);
        while (child != null)
        {
            var childNode = WalkTree(child, maxDepth, depth + 1);
            if (childNode != null)
                node.Children.Add(childNode);
            child = walker.GetNextSibling(child);
        }
        return node;
    }

    // クリック座標からUIAの要素を逆引き
    public UIATreeNode FindElementAtPoint(int screenX, int screenY)
    {
        try
        {
            var element = AutomationElement.FromPoint(
                new System.Windows.Point(screenX, screenY));
            return new UIATreeNode
            {
                AutomationId = element.Current.AutomationId,
                Name = element.Current.Name,
                ControlType = element.Current.ControlType.ProgrammaticName,
                BoundingRect = element.Current.BoundingRectangle
            };
        }
        catch
        {
            return null;  // UIAで取得できない → 画像認識にフォールバック
        }
    }
}
```

### 5.5 `wfth-aggregate` + `wfth-correlate`（統合の要）

旧設計の単一 `EventCorrelator` は責務過多のため、**集約** と **相関** に分割する。

```csharp
public sealed class CorrelateCommand
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        await foreach (var action in _aggregateReader.ReadAsync(ct)) // stdin
        {
            var uia = _uiaIndex.FindWithinWindow(action.Timestamp, _window);
            var screenshots = _screenshotIndex.FindFor(action);
            var appLog = _appLogIndex.FindWithinWindow(action.Timestamp, _window);

            var noise = _noiseClassifier.Classify(action, uia, appLog, screenshots);
            if (noise != null && !_includeNoise && noise.Confidence >= _noiseThreshold)
                continue;

            _writer.Write(new CorrelatedAction
            {
                Seq = ++_seq,
                Timestamp = action.Timestamp,
                Type = action.Type,
                Input = action.Input,
                Target = ResolveTarget(action, uia),
                Screenshots = screenshots,
                UiaDiff = uia?.Diff,
                AppLog = appLog,
                Noise = noise?.Type,
                Confidence = noise?.Confidence
            });
        }
        return 0;
    }
}
```

### 5.6 IPC（アプリ内ロガーとの連携）

名前付きパイプで外部プロセスとアプリ内ロガーを接続する。

```csharp
// アプリ内ロガー側（送信）
public class IpcLogSink : ILogSink
{
    private readonly NamedPipeClientStream _pipe;

    public IpcLogSink(int recorderPid, string sessionNonce)
    {
        _pipe = new NamedPipeClientStream(
            ".", $"WinFormsTestHarness_{recorderPid}_{sessionNonce}",
            PipeDirection.Out, PipeOptions.Asynchronous);
    }
}

// Recording Engine側（受信）
public class IpcReceiver
{
    // ACL: 同一ユーザーSIDのみ許可
    // ハンドシェイク: hello/challenge/response で sessionToken を検証
    // 失敗時: ipc_auth_failed をNDJSONに出力して切断
}
```

---

## 6. 統合ログ出力形式

AIエージェントへの入力となる統合ログの形式。**標準は NDJSON**（1行1アクション）。

```json
{"seq":1,"ts":"2026-02-22T14:30:05.123Z","type":"Click","input":{"button":"Left","sx":450,"sy":320,"rx":230,"ry":180},"target":{"source":"UIA","automationId":"btnCustomerSearch","name":"顧客検索","controlType":"Button"},"screenshots":{"before":"screenshots/001_before.png","after":"screenshots/001_after.png"},"uiaDiff":{"added":[{"name":"検索","controlType":"Window"}]},"appLog":[{"type":"event","control":"btnCustomerSearch","event":"Click"}]}
{"seq":2,"ts":"2026-02-22T14:30:08.456Z","type":"TextInput","input":{"text":"田中"},"target":{"source":"UIA","automationId":"txtSearchCondition","name":"検索条件","controlType":"Edit"},"screenshots":{"after":"screenshots/002_after.png"},"appLog":[{"type":"prop","control":"txtSearchCondition","prop":"Text","new":"田中"}]}
```

モノリシック JSON が必要な場合は `jq -s` または将来の `wfth-session` で変換する。

### AIエージェントが統合ログから得られる情報

| 情報 | ソース | 用途 |
|------|--------|------|
| 何をしたか | Input（クリック、テキスト入力） | 操作の再現 |
| 何に対してしたか | target（UIA情報） | 安定した要素特定 |
| 画面がどう見えたか | Screenshots | 画像認識フォールバック、期待結果の検証 |
| UIがどう変化したか | uiaDiff | 画面遷移の検出、アサーション生成 |
| アプリ内部で何が起きたか | appLog | 操作の意図の正確な理解 |

---

## 7. テスト仕様書の活用（設計方針のみ）

### 現場の典型的なテスト仕様書構造

```
テストケースID: TC-001
前提条件: ログイン済み、メイン画面表示
手順:
  1. 「顧客検索」ボタンをクリック
  2. 検索条件に「田中」と入力
  3. 「検索」ボタンをクリック
期待結果:
  - 検索結果一覧に「田中太郎」が表示される
  - 件数表示が「1件」となる
```

### 構造化データへの変換（想定）

```json
{
  "test_case_id": "TC-001",
  "preconditions": ["logged_in", "main_form_visible"],
  "steps": [
    {
      "action": "click",
      "target_description": "顧客検索ボタン",
      "sequence": 1
    },
    {
      "action": "input",
      "target_description": "検索条件",
      "value": "田中",
      "sequence": 2
    },
    {
      "action": "click",
      "target_description": "検索ボタン",
      "sequence": 3
    }
  ],
  "expected_results": [
    { "type": "text_present", "value": "田中太郎", "location": "検索結果一覧" },
    { "type": "text_equals", "value": "1件", "location": "件数表示" }
  ]
}
```

**注意**: この文書では仕様書パーサーは概要のみ扱う。詳細は `spec-parser-design.md` を参照。

---

## 8. パフォーマンス配慮

| 懸念点 | 対策 |
|--------|------|
| スクリーンショットの頻度と容量 | 差分検知で無変化時はスキップ。PNG圧縮、一定サイズにリサイズ |
| UIAツリー走査の遅延 | 入力イベント時のみ全走査。クリック地点のみは FromPoint で高速取得 |
| グローバルフックのパフォーマンス影響 | 対象ウィンドウ判定を最初に行い、対象外は即座にCallNextHookEx |
| 名前付きパイプのスループット | 非同期書き込み、バッファリング。ログはバッチで送信 |

---

## 9. 未設計・今後の検討事項

### Recording Engine周り（詳細設計済み）

- 信頼性・安定性: `recording-reliability-design.md`
- データ品質/容量: `recording-data-quality-design.md`
- 外部連携（IPC/CI）: `recording-integration-design.md`
- キャプチャ統合: `capture-design.md`
- 集約/相関分割: `correlate-split-design.md`

### Recording Engine周り（未設計/残課題）

- [x] グローバルフックがクラッシュした場合のリカバリ → `recording-reliability-design.md`
- [x] 対象アプリが応答なし（ハング）になった場合の挙動 → `recording-reliability-design.md`
- [x] マルチウィンドウ・モーダルダイアログの追跡 → `recording-reliability-design.md`
- [x] 高DPI環境や複数モニタでの座標ズレ → `recording-reliability-design.md`
- [ ] 開始・停止・一時停止のワークフロー設計（開始・停止は `recording-cli-design.md` で設計済み、一時停止は未設計）
- [ ] テスト仕様書のステップとRecordingの紐付けタイミング（概念設計は `spec-parser-design.md` にあるが、`--spec` 統合は将来機能）
- [ ] 手動テスターが使うUI（Recording Controller）の設計
- [x] ノイズの除去（意図しないクリック、操作ミスの扱い） → `recording-data-quality-design.md`
- [x] スクリーンショットの保存戦略（容量と解像度のバランス） → `capture-design.md`, `recording-data-quality-design.md`
- [x] UIAで取れない要素の検出と画像認識用リファレンス画像の自動抽出 → `recording-data-quality-design.md`
- [x] アプリ内ロガーとの時刻同期・イベント突合の精度 → `recording-integration-design.md`
- [x] CIでのヘッドレス実行の可能性 → `recording-integration-design.md`

### テスト仕様書パーサー

- [ ] Excel/Word等の各形式への対応（`.xlsx` は `spec-parser-design.md` で設計済み、Word/PDF は将来拡張）
- [x] 非定型な仕様書フォーマットへの対応戦略 → `spec-parser-design.md`
- [ ] AIによる仕様書解釈の精度と限界（設計は `spec-parser-design.md` で完了、実運用評価は将来）

### AIエージェント（意図的にスコープ外）

- モデル選択とプロンプト設計は現時点では深掘りしない
- 理由: モデル自身の成長が著しく、多少の問題はスケールアップとプロンプトチューニングで解決でき、それ以上の問題は個人でアプローチしても解決可能性が低い

---

## 10. 技術スタック（想定）

| コンポーネント | 技術 |
|---------------|------|
| テストコード出力 | C# |
| テストランナー | NUnit / xUnit / MSTest |
| UI Automation | System.Windows.Automation (UIA COM) |
| 画像認識 | 未定（OpenCV、Windows.Media.Ocr、外部AI等） |
| アプリ内ロガー | C# NuGet パッケージ |
| Recording Engine | C# 外部プロセス |
| IPC | 名前付きパイプ (NamedPipeStream) |
| 統合ログ | NDJSON（標準） / JSON（変換段で生成） |
| スクリーンショット | PNG |

---

## 11. プロジェクト構成

```
WinFormsTestHarness/
├── src/
│   ├── WinFormsTestHarness.Common/        # 共通ライブラリ（NDJSON I/O, ExitCodes, JsonHelper等）
│   ├── WinFormsTestHarness.Inspect/       # wfth-inspect — UIAツリー偵察CLI（実装済み）
│   ├── WinFormsTestHarness.Record/        # wfth-record — 入力イベント記録
│   ├── WinFormsTestHarness.Capture/       # スクリーンショットキャプチャ共有ライブラリ（classlib）
│   ├── WinFormsTestHarness.Capture.Cli/   # wfth-capture — スクリーンショットCLIラッパー
│   ├── WinFormsTestHarness.Aggregate/     # wfth-aggregate — 生イベント集約
│   ├── WinFormsTestHarness.Correlate/     # wfth-correlate — 時間窓相関
│   ├── WinFormsTestHarness.Core/          # テスト実行フレームワーク（ドライバー層 + 操作抽象化層）
│   └── WinFormsTestHarness.Logger/        # アプリ内ロガー NuGet パッケージ
├── tests/
│   └── WinFormsTestHarness.Tests/         # フレームワーク自体のテスト
├── samples/
│   └── SampleApp/                         # テスト対象サンプルアプリ
├── demo/                                  # パイプライン検証用デモデータ
└── doc/                                   # 設計ドキュメント
```

> **Note**: テスト仕様書パーサー（SpecParser）は Phase 2（仕様書駆動テスト生成）の将来構想であり、プロジェクトは未作成。
