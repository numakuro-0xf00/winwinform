# WinForms E2E テスト自動化プラットフォーム — 設計ドキュメント

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
┌──────────┐    ┌─────────────────────────┐
│ 仕様書    │    │   Recording Engine       │
│ パーサー  │    │                           │
│          │    │  ┌─────────┐ ┌─────────┐ │
└────┬─────┘    │  │ 操作     │ │ アプリ内 │ │
     │          │  │ キャプチャ│ │ ロガー   │ │
     │          │  └────┬────┘ └────┬────┘ │
     │          └───────┼───────────┼──────┘
     │                  │           │
     ▼                  ▼           ▼
┌──────────────────────────────────────────┐
│        統合ログストア                      │
│  操作ログ + スクリーンショット + 内部状態   │
│  + テスト仕様のステップ情報                │
└──────────────────┬───────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────┐
│     AI エージェント (コード生成)            │
│  ログ + 仕様書 → テストスクリプト生成      │
└──────────────────┬───────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────┐
│     回帰テストスイート                     │
│  UI Automation + 画像認識 ハイブリッド実行  │
└──────────────────────────────────────────┘
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
┌─────────────────────────────────────────────────────────┐
│                  Recording Engine (外部プロセス)          │
│                                                           │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────┐ │
│  │ Input Hook    │  │ Screen       │  │ UIA Tree       │ │
│  │ Manager       │  │ Capturer     │  │ Snapshotter    │ │
│  │               │  │              │  │                │ │
│  │ マウス/KB     │  │ スクリーン    │  │ UIツリー       │ │
│  │ グローバル     │  │ ショット     │  │ スナップショット │ │
│  │ フック        │  │ + 差分検知   │  │ + 差分検出     │ │
│  └──────┬───────┘  └──────┬───────┘  └───────┬────────┘ │
│         │                 │                   │          │
│         ▼                 ▼                   ▼          │
│  ┌──────────────────────────────────────────────────┐   │
│  │              Event Correlator                     │   │
│  │    入力イベント + スクリーンショット + UIツリー      │   │
│  │    を時系列で紐付けて統合ログを生成                 │   │
│  └──────────────────────┬───────────────────────────┘   │
│                         │                                │
│                         ▼                                │
│  ┌──────────────────────────────────────────────────┐   │
│  │              Log Writer                           │   │
│  │    統合ログ + アプリ内ロガーのログをマージ          │   │
│  └──────────────────────────────────────────────────┘   │
│                                                           │
│  ┌──────────────────────────────────────────────────┐   │
│  │        IPC Receiver（アプリ内ロガーからの受信）     │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
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

### キーボード入力のシーケンス集約

個別のキーイベントではなく、文字列入力としてまとめて記録する。

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

### 5.4 UIA Tree Snapshotter

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

### 5.5 Event Correlator（統合の要）

各コンポーネントの出力を時系列で紐付けて統合ログを生成する。

```csharp
public class EventCorrelator
{
    private readonly InputHookManager _inputHook;
    private readonly ScreenCapturer _screenCapturer;
    private readonly UIATreeSnapshotter _uiaSnapshotter;
    private readonly CaptureStrategy _captureStrategy;
    private readonly LogWriter _logWriter;

    public async Task StartRecording(CancellationToken ct)
    {
        _inputHook.MouseAction += async (s, e) =>
        {
            // 1. クリック座標のUIA要素を特定
            var uiaElement = _uiaSnapshotter.FindElementAtPoint(e.ScreenX, e.ScreenY);

            // 2. スクリーンショット（操作前後）
            var screenshot = await _captureStrategy
                .CaptureOnInputEvent($"mouse_{e.Action}");

            // 3. UIAツリーの差分
            var treeSnapshot = _uiaSnapshotter
                .TakeSnapshot($"mouse_{e.Action}");

            // 4. 統合RecordedActionとして記録
            _logWriter.Write(new RecordedAction
            {
                Timestamp = e.Timestamp,
                Type = ActionType.MouseClick,
                Input = new InputData
                {
                    MouseAction = e.Action.ToString(),
                    ScreenCoordinate = (e.ScreenX, e.ScreenY),
                    RelativeCoordinate = (e.RelativeX, e.RelativeY)
                },
                TargetElement = uiaElement != null
                    ? new TargetElementData
                    {
                        Source = "UIA",
                        AutomationId = uiaElement.AutomationId,
                        Name = uiaElement.Name,
                        ControlType = uiaElement.ControlType,
                        BoundingRect = uiaElement.BoundingRect
                    }
                    : new TargetElementData
                    {
                        Source = "Coordinate",
                        Note = "UIA要素特定不可。画像認識が必要"
                    },
                Screenshots = screenshot,
                UIATreeDiff = treeSnapshot.Diff
            });
        };

        // 定期的な画面遷移監視も並行実行
        await _captureStrategy.MonitorForTransitions(ct);
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

    public IpcLogSink()
    {
        _pipe = new NamedPipeClientStream(".", "WinFormsTestHarness",
            PipeDirection.Out, PipeOptions.Asynchronous);
    }

    public void Write(LogEntry entry)
    {
        var json = JsonSerializer.Serialize(entry);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        _pipe.WriteAsync(bytes);
    }
}

// Recording Engine側（受信）
public class IpcReceiver
{
    private readonly NamedPipeServerStream _pipe;
    public event EventHandler<LogEntry> AppLogReceived;

    public async Task ListenAsync(CancellationToken ct)
    {
        _pipe = new NamedPipeServerStream("WinFormsTestHarness",
            PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await _pipe.WaitForConnectionAsync(ct);

        using var reader = new StreamReader(_pipe);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line != null)
            {
                var entry = JsonSerializer.Deserialize<LogEntry>(line);
                AppLogReceived?.Invoke(this, entry);
            }
        }
    }
}
```

---

## 6. 統合ログ出力形式

AIエージェントへの入力となる統合ログの形式。

```json
{
  "session": {
    "id": "rec-20260222-001",
    "targetApp": "CustomerManager.exe",
    "startedAt": "2026-02-22T14:30:00Z",
    "testCaseRef": "TC-001"
  },
  "actions": [
    {
      "sequence": 1,
      "timestamp": "2026-02-22T14:30:05.123Z",
      "type": "MouseClick",
      "input": {
        "action": "LeftClick",
        "screenCoordinate": [450, 320],
        "relativeCoordinate": [230, 180]
      },
      "targetElement": {
        "source": "UIA",
        "automationId": "btnCustomerSearch",
        "name": "顧客検索",
        "controlType": "Button",
        "boundingRect": { "x": 420, "y": 310, "w": 80, "h": 30 }
      },
      "screenshots": {
        "before": "screenshots/001_before.png",
        "after": "screenshots/001_after.png"
      },
      "uiaTreeDiff": {
        "added": [
          { "automationId": "", "name": "検索", "controlType": "Window",
            "note": "SearchForm が新たに出現" }
        ]
      },
      "appLoggerEvents": [
        { "type": "Event", "control": "btnCustomerSearch", "event": "Click" },
        { "type": "FormOpened", "form": "SearchForm" }
      ]
    },
    {
      "sequence": 2,
      "timestamp": "2026-02-22T14:30:08.456Z",
      "type": "TextInput",
      "input": {
        "text": "田中"
      },
      "targetElement": {
        "source": "UIA",
        "automationId": "txtSearchCondition",
        "name": "検索条件",
        "controlType": "Edit"
      },
      "screenshots": {
        "after": "screenshots/002_after.png"
      },
      "appLoggerEvents": [
        { "type": "PropertyChanged", "control": "txtSearchCondition",
          "property": "Text", "oldValue": "", "newValue": "田中" }
      ]
    }
  ]
}
```

### AIエージェントが統合ログから得られる情報

| 情報 | ソース | 用途 |
|------|--------|------|
| 何をしたか | Input（クリック、テキスト入力） | 操作の再現 |
| 何に対してしたか | TargetElement（UIA情報） | 安定した要素特定 |
| 画面がどう見えたか | Screenshots | 画像認識フォールバック、期待結果の検証 |
| UIがどう変化したか | UIATreeDiff | 画面遷移の検出、アサーション生成 |
| アプリ内部で何が起きたか | AppLoggerEvents | 操作の意図の正確な理解 |

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

**注意**: テスト仕様書パーサーの詳細設計は未着手。

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

### Recording Engine周り（優先度高）

- グローバルフックがクラッシュした場合のリカバリ
- 対象アプリが応答なし（ハング）になった場合の挙動
- マルチウィンドウ・モーダルダイアログの追跡
- 高DPI環境や複数モニタでの座標ズレ
- 開始・停止・一時停止のワークフロー設計
- テスト仕様書のステップとRecordingの紐付けタイミング
- 手動テスターが使うUI（Recording Controller）の設計
- ノイズの除去（意図しないクリック、操作ミスの扱い）
- スクリーンショットの保存戦略（容量と解像度のバランス）
- UIAで取れない要素の検出と画像認識用リファレンス画像の自動抽出
- アプリ内ロガーとの時刻同期・イベント突合の精度
- CIでのヘッドレス実行の可能性

### テスト仕様書パーサー（詳細設計未着手）

- Excel/Word等の各形式への対応
- 非定型な仕様書フォーマットへの対応戦略
- AIによる仕様書解釈の精度と限界

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
| 統合ログ | JSON形式 |
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
