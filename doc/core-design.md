# WinFormsTestHarness.Core 設計

## 1. 概要

テスト実行フレームワーク本体。**ドライバー層** と **操作抽象化層** を提供し、AIが生成するテストコードからUI操作の実装詳細を隠蔽する。

```
テストケース層（AI生成、Coreの外）
  ↓ 利用
操作抽象化層（Core）
  ├── FormPage / IElement / Strategy — Page Objectパターン
  ├── AppInstance — アプリのライフサイクル管理
  └── Assertions — Fluent検証API
  ↓ 利用
ドライバー層（Core）
  ├── HybridElementLocator — フォールバック付き要素特定
  ├── UIAutomationDriver — FlaUI.UIA3 ベース
  ├── ImageRecognitionDriver — 画像認識ベース
  └── ActionExecutor — UI操作の実行（クリック、テキスト入力等）
```

### 依存関係

```
WinFormsTestHarness.Core
  ├── FlaUI.UIA3                        — UIA操作
  ├── WinFormsTestHarness.Capture       — スクリーンショット（失敗時証跡）
  └── System.Drawing.Common            — 画像処理
```

テストプロジェクト側:
```
SampleTests.csproj
  ├── NUnit / xUnit / MSTest            — テストランナー
  └── WinFormsTestHarness.Core          — このパッケージ
```

---

## 2. ドライバー層

### 2.1 要素特定の共通インターフェース

```csharp
/// <summary>UIツリーから要素を特定するドライバー</summary>
public interface IElementDriver
{
    /// <summary>ストラテジーに基づいて要素を検索</summary>
    Task<FoundElement?> FindAsync(ElementStrategy strategy, TimeSpan timeout);

    /// <summary>このドライバーが対応するストラテジー種別</summary>
    IReadOnlySet<StrategyKind> SupportedStrategies { get; }
}

/// <summary>特定された要素の情報</summary>
public class FoundElement
{
    public string AutomationId { get; init; } = "";
    public string Name { get; init; } = "";
    public string ControlType { get; init; } = "";
    public Rectangle BoundingRect { get; init; }
    public string LocatedBy { get; init; } = "";       // どのストラテジーで見つけたか

    /// <summary>UIA要素への参照（UIA操作用、UIADriver由来の場合のみ）</summary>
    internal AutomationElement? UiaElement { get; init; }

    /// <summary>画像認識の座標（ImageDriver由来の場合のみ）</summary>
    internal Point? ImageCenter { get; init; }
}
```

### 2.2 UIAutomationDriver

FlaUI.UIA3 を使ってUIAツリーから要素を特定・操作する。wfth-inspect と同じ FlaUI バックエンドを共有。

```csharp
public class UIAutomationDriver : IElementDriver, IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly AutomationElement _rootElement;

    public UIAutomationDriver(IntPtr hwnd)
    {
        _automation = new UIA3Automation();
        _rootElement = _automation.FromHandle(hwnd);
    }

    public IReadOnlySet<StrategyKind> SupportedStrategies { get; } =
        new HashSet<StrategyKind>
        {
            StrategyKind.ByAutomationId,
            StrategyKind.ByName,
            StrategyKind.ByControlType,
            StrategyKind.ByClassName,
            StrategyKind.ByPath,
        };

    public async Task<FoundElement?> FindAsync(ElementStrategy strategy, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var element = strategy.Kind switch
            {
                StrategyKind.ByAutomationId => FindByAutomationId(strategy.Value),
                StrategyKind.ByName => FindByName(strategy.Value, strategy.ControlType),
                StrategyKind.ByControlType => FindByControlType(strategy.Value),
                StrategyKind.ByClassName => FindByClassName(strategy.Value),
                StrategyKind.ByPath => FindByPath(strategy.Value),
                _ => null
            };

            if (element != null)
                return element;

            await Task.Delay(200);
        }

        return null;
    }

    private FoundElement? FindByAutomationId(string automationId)
    {
        var condition = _rootElement.ConditionFactory.ByAutomationId(automationId);
        var element = _rootElement.FindFirstDescendant(condition);
        return element != null ? ToFoundElement(element, $"AutomationId={automationId}") : null;
    }

    private FoundElement? FindByName(string name, string? controlType)
    {
        var conditions = new List<ConditionBase>
        {
            _rootElement.ConditionFactory.ByName(name)
        };
        if (controlType != null)
        {
            conditions.Add(_rootElement.ConditionFactory.ByControlType(
                Enum.Parse<FlaUI.Core.Definitions.ControlType>(controlType)));
        }

        var condition = conditions.Count == 1
            ? conditions[0]
            : new AndCondition(conditions.ToArray());
        var element = _rootElement.FindFirstDescendant(condition);
        return element != null ? ToFoundElement(element, $"Name={name}") : null;
    }

    // ... FindByControlType, FindByClassName, FindByPath は同パターン

    private static FoundElement ToFoundElement(AutomationElement element, string locatedBy)
    {
        var rect = element.BoundingRectangle;
        return new FoundElement
        {
            AutomationId = element.Properties.AutomationId.ValueOrDefault ?? "",
            Name = element.Properties.Name.ValueOrDefault ?? "",
            ControlType = element.Properties.ControlType.ValueOrDefault.ToString() ?? "",
            BoundingRect = new Rectangle(
                (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height),
            LocatedBy = locatedBy,
            UiaElement = element,
        };
    }
}
```

### 2.3 ImageRecognitionDriver

画像認識で要素を特定する。UIAで見つからない場合のフォールバック。

```csharp
public class ImageRecognitionDriver : IElementDriver, IDisposable
{
    private readonly ScreenCapturer _capturer;
    private readonly IImageMatcher _matcher;

    public IReadOnlySet<StrategyKind> SupportedStrategies { get; } =
        new HashSet<StrategyKind>
        {
            StrategyKind.ByImage,
            StrategyKind.ByPattern,
        };

    public async Task<FoundElement?> FindAsync(ElementStrategy strategy, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var screenshot = _capturer.Capture("element_search");

            var result = strategy.Kind switch
            {
                StrategyKind.ByImage => _matcher.FindTemplate(
                    screenshot.Bitmap, strategy.ReferenceImage!),
                StrategyKind.ByPattern => _matcher.FindByOcr(
                    screenshot.Bitmap, strategy.Value),
                _ => null
            };

            screenshot.Bitmap.Dispose();

            if (result != null)
            {
                return new FoundElement
                {
                    Name = strategy.Description ?? strategy.Value,
                    ControlType = "Unknown",
                    BoundingRect = result.MatchRect,
                    LocatedBy = $"Image:{strategy.Value}",
                    ImageCenter = result.Center,
                };
            }

            await Task.Delay(500);
        }

        return null;
    }
}
```

### 2.4 IImageMatcher — 画像認識の抽象化

画像認識の具体実装を差し替え可能にする。

```csharp
public interface IImageMatcher
{
    /// <summary>テンプレートマッチングで参照画像を検索</summary>
    ImageMatchResult? FindTemplate(Bitmap screenshot, Bitmap template, double threshold = 0.85);

    /// <summary>OCRでテキストパターンを検索</summary>
    ImageMatchResult? FindByOcr(Bitmap screenshot, string pattern);
}

public class ImageMatchResult
{
    public Rectangle MatchRect { get; init; }
    public Point Center { get; init; }
    public double Confidence { get; init; }
}
```

初期実装（MVP E）:

```
NullImageMatcher     — 常にnullを返すスタブ（画像認識未実装時）
SimpleImageMatcher   — System.Drawing の ピクセル比較ベース（低精度だが依存なし）
```

将来拡張:
```
OpenCvImageMatcher   — OpenCvSharp によるテンプレートマッチング（高精度）
WinRtOcrMatcher      — Windows.Media.Ocr による OCR
ExternalAiMatcher    — 外部AI Vision API による要素特定
```

### 2.5 HybridElementLocator — フォールバックチェーン

```csharp
/// <summary>
/// 複数ストラテジーを優先順位付きで試行し、最初に見つかった要素を返す。
/// 全ストラテジー失敗時は詳細なエラーレポートを生成する。
/// </summary>
public class HybridElementLocator
{
    private readonly IReadOnlyList<IElementDriver> _drivers;
    private readonly ScreenCapturer? _capturer;
    private readonly TimeSpan _defaultTimeout;

    public HybridElementLocator(
        IReadOnlyList<IElementDriver> drivers,
        ScreenCapturer? capturer = null,
        TimeSpan? defaultTimeout = null)
    {
        _drivers = drivers;
        _capturer = capturer;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task<FoundElement> FindAsync(
        ElementStrategy[] strategies,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? _defaultTimeout;
        var perStrategyTimeout = TimeSpan.FromMilliseconds(
            effectiveTimeout.TotalMilliseconds / strategies.Length);
        var failures = new List<StrategyFailure>();

        foreach (var strategy in strategies)
        {
            var driver = _drivers.FirstOrDefault(d =>
                d.SupportedStrategies.Contains(strategy.Kind));

            if (driver == null)
            {
                failures.Add(new StrategyFailure(strategy, "対応ドライバーなし"));
                continue;
            }

            try
            {
                var element = await driver.FindAsync(strategy, perStrategyTimeout);
                if (element != null)
                    return element;

                failures.Add(new StrategyFailure(strategy, "要素が見つからない"));
            }
            catch (Exception ex)
            {
                failures.Add(new StrategyFailure(strategy, ex.Message));
            }
        }

        // 全ストラテジー失敗
        var screenshot = _capturer?.Capture("all_strategies_failed");
        throw new ElementNotFoundException(strategies, failures, screenshot?.FilePath);
    }
}
```

### 2.6 ActionExecutor — UI操作の実行

```csharp
/// <summary>特定された要素に対してUI操作を実行する</summary>
public class ActionExecutor
{
    private readonly UIAutomationDriver _uiaDriver;

    /// <summary>クリック</summary>
    public async Task ClickAsync(FoundElement element)
    {
        if (element.UiaElement != null)
        {
            // UIA経由: InvokePattern または TogglePattern を試行
            if (element.UiaElement.TryGetInvokePattern(out var invoke))
            {
                invoke.Invoke();
                return;
            }
            if (element.UiaElement.TryGetTogglePattern(out var toggle))
            {
                toggle.Toggle();
                return;
            }
        }

        // フォールバック: マウスクリック
        var center = element.ImageCenter
            ?? new Point(
                element.BoundingRect.X + element.BoundingRect.Width / 2,
                element.BoundingRect.Y + element.BoundingRect.Height / 2);
        await MouseHelper.ClickAsync(center);
    }

    /// <summary>テキスト入力</summary>
    public async Task SetTextAsync(FoundElement element, string text)
    {
        if (element.UiaElement != null)
        {
            // UIA経由: ValuePattern
            if (element.UiaElement.TryGetValuePattern(out var value))
            {
                value.SetValue(text);
                return;
            }
        }

        // フォールバック: フォーカス→キーボード入力
        await ClickAsync(element);
        await Task.Delay(50);
        SendKeys.SendWait("^a");        // 全選択
        await Task.Delay(50);
        SendKeys.SendWait(text);
    }

    /// <summary>テキスト取得</summary>
    public string GetText(FoundElement element)
    {
        if (element.UiaElement != null)
        {
            if (element.UiaElement.TryGetValuePattern(out var value))
                return value.Value;
            // Name プロパティをフォールバック
            return element.Name;
        }

        // 画像認識要素: OCR が必要（将来実装）
        throw new NotSupportedException(
            "画像認識で特定した要素からのテキスト取得は未実装です");
    }

    /// <summary>選択操作（ComboBox、ListBox等）</summary>
    public async Task SelectAsync(FoundElement element, string itemText)
    {
        if (element.UiaElement != null)
        {
            if (element.UiaElement.TryGetExpandCollapsePattern(out var expand))
            {
                expand.Expand();
                await Task.Delay(200);
            }

            // 子要素からテキスト一致するアイテムを検索
            var item = element.UiaElement.FindFirstDescendant(
                cf => cf.ByName(itemText));
            if (item != null)
            {
                if (item.TryGetSelectionItemPattern(out var selectItem))
                {
                    selectItem.Select();
                    return;
                }
            }
        }

        // フォールバック: クリック→テキスト入力
        await ClickAsync(element);
        await Task.Delay(100);
        SendKeys.SendWait(itemText);
        SendKeys.SendWait("{ENTER}");
    }

    /// <summary>チェックボックスの状態設定</summary>
    public async Task SetCheckedAsync(FoundElement element, bool isChecked)
    {
        if (element.UiaElement != null)
        {
            if (element.UiaElement.TryGetTogglePattern(out var toggle))
            {
                var current = toggle.ToggleState == ToggleState.On;
                if (current != isChecked)
                    toggle.Toggle();
                return;
            }
        }

        // フォールバック
        await ClickAsync(element);
    }
}
```

### 2.7 MouseHelper / KeyboardHelper

```csharp
/// <summary>Win32 API によるマウス操作</summary>
internal static class MouseHelper
{
    public static async Task ClickAsync(Point screenPoint, MouseButton button = MouseButton.Left)
    {
        NativeMethods.SetCursorPos(screenPoint.X, screenPoint.Y);
        await Task.Delay(30);

        var (downFlag, upFlag) = button switch
        {
            MouseButton.Left => (MOUSEEVENTF.LEFTDOWN, MOUSEEVENTF.LEFTUP),
            MouseButton.Right => (MOUSEEVENTF.RIGHTDOWN, MOUSEEVENTF.RIGHTUP),
            MouseButton.Middle => (MOUSEEVENTF.MIDDLEDOWN, MOUSEEVENTF.MIDDLEUP),
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };

        NativeMethods.mouse_event(downFlag, 0, 0, 0, 0);
        await Task.Delay(30);
        NativeMethods.mouse_event(upFlag, 0, 0, 0, 0);
    }

    public static async Task DoubleClickAsync(Point screenPoint)
    {
        await ClickAsync(screenPoint);
        await Task.Delay(50);
        await ClickAsync(screenPoint);
    }

    public static async Task DragAsync(Point from, Point to, int stepCount = 10)
    {
        NativeMethods.SetCursorPos(from.X, from.Y);
        await Task.Delay(30);
        NativeMethods.mouse_event(MOUSEEVENTF.LEFTDOWN, 0, 0, 0, 0);

        for (int i = 1; i <= stepCount; i++)
        {
            var x = from.X + (to.X - from.X) * i / stepCount;
            var y = from.Y + (to.Y - from.Y) * i / stepCount;
            NativeMethods.SetCursorPos(x, y);
            await Task.Delay(15);
        }

        NativeMethods.mouse_event(MOUSEEVENTF.LEFTUP, 0, 0, 0, 0);
    }
}
```

---

## 3. 操作抽象化層

### 3.1 ElementStrategy — 要素特定ストラテジー

```csharp
public enum StrategyKind
{
    ByAutomationId,   // UIA AutomationId
    ByName,           // UIA Name プロパティ
    ByControlType,    // UIA ControlType
    ByClassName,      // UIA ClassName
    ByPath,           // UIA ツリーパス (例: "Window/Form/Panel/TextBox")
    ByImage,          // テンプレートマッチング
    ByPattern,        // OCR + 正規表現パターン
}

public class ElementStrategy
{
    public StrategyKind Kind { get; init; }
    public string Value { get; init; } = "";
    public string? ControlType { get; init; }       // ByName の絞り込み用
    public string? Description { get; init; }        // ログ/エラー表示用
    public Bitmap? ReferenceImage { get; init; }     // ByImage 用
}

/// <summary>ストラテジー生成ヘルパー</summary>
public static class Strategy
{
    public static ElementStrategy ByAutomationId(string id)
        => new() { Kind = StrategyKind.ByAutomationId, Value = id,
                   Description = $"AutomationId={id}" };

    public static ElementStrategy ByName(string name, string? controlType = null)
        => new() { Kind = StrategyKind.ByName, Value = name, ControlType = controlType,
                   Description = $"Name={name}" };

    public static ElementStrategy ByControlType(string controlType)
        => new() { Kind = StrategyKind.ByControlType, Value = controlType,
                   Description = $"ControlType={controlType}" };

    public static ElementStrategy ByClassName(string className)
        => new() { Kind = StrategyKind.ByClassName, Value = className,
                   Description = $"ClassName={className}" };

    public static ElementStrategy ByPath(string path)
        => new() { Kind = StrategyKind.ByPath, Value = path,
                   Description = $"Path={path}" };

    public static ElementStrategy ByImage(string imagePath)
    {
        var bitmap = new Bitmap(imagePath);
        return new()
        {
            Kind = StrategyKind.ByImage, Value = imagePath,
            ReferenceImage = bitmap, Description = $"Image={imagePath}"
        };
    }

    public static ElementStrategy ByPattern(string regexPattern)
        => new() { Kind = StrategyKind.ByPattern, Value = regexPattern,
                   Description = $"Pattern={regexPattern}" };
}
```

### 3.2 IElement — 要素操作インターフェース

テストコードが触る主要なインターフェース。遅延評価で要素を特定し、操作する。

```csharp
/// <summary>UI要素を表す。操作時に初めて要素を検索する（遅延バインド）。</summary>
public interface IElement
{
    /// <summary>クリック</summary>
    Task ClickAsync();

    /// <summary>ダブルクリック</summary>
    Task DoubleClickAsync();

    /// <summary>右クリック</summary>
    Task RightClickAsync();

    /// <summary>テキスト入力（既存テキストを置換）</summary>
    Task SetTextAsync(string text);

    /// <summary>テキスト取得</summary>
    Task<string> GetTextAsync();

    /// <summary>選択（ComboBox、ListBox等）</summary>
    Task SelectAsync(string itemText);

    /// <summary>チェック状態設定</summary>
    Task SetCheckedAsync(bool isChecked);

    /// <summary>チェック状態取得</summary>
    Task<bool> GetCheckedAsync();

    /// <summary>有効/無効状態を取得</summary>
    Task<bool> IsEnabledAsync();

    /// <summary>表示/非表示状態を取得</summary>
    Task<bool> IsVisibleAsync();

    /// <summary>検証用ヘルパー</summary>
    IElementAssertions Should();

    /// <summary>要素が存在するまで待機（タイムアウトで例外）</summary>
    Task<IElement> WaitUntilExistsAsync(TimeSpan? timeout = null);

    /// <summary>要素の内部情報を取得（デバッグ用）</summary>
    Task<FoundElement> ResolveAsync();
}
```

### 3.3 Element 実装

```csharp
/// <summary>遅延バインド + キャッシュ付きの要素ラッパー</summary>
internal class Element : IElement
{
    private readonly HybridElementLocator _locator;
    private readonly ActionExecutor _executor;
    private readonly ElementStrategy[] _strategies;
    private readonly TimeSpan _timeout;

    private FoundElement? _cached;

    internal Element(
        HybridElementLocator locator,
        ActionExecutor executor,
        ElementStrategy[] strategies,
        TimeSpan timeout)
    {
        _locator = locator;
        _executor = executor;
        _strategies = strategies;
        _timeout = timeout;
    }

    public async Task<FoundElement> ResolveAsync()
    {
        // キャッシュが有効かチェック（要素がまだ画面上に存在するか）
        if (_cached != null && IsStillValid(_cached))
            return _cached;

        _cached = await _locator.FindAsync(_strategies, _timeout);
        return _cached;
    }

    public async Task ClickAsync()
    {
        var found = await ResolveAsync();
        await _executor.ClickAsync(found);
        _cached = null;  // クリック後は画面が変わる可能性があるためキャッシュ無効化
    }

    public async Task SetTextAsync(string text)
    {
        var found = await ResolveAsync();
        await _executor.SetTextAsync(found, text);
    }

    public async Task<string> GetTextAsync()
    {
        var found = await ResolveAsync();
        return _executor.GetText(found);
    }

    public async Task SelectAsync(string itemText)
    {
        var found = await ResolveAsync();
        await _executor.SelectAsync(found, itemText);
    }

    public async Task SetCheckedAsync(bool isChecked)
    {
        var found = await ResolveAsync();
        await _executor.SetCheckedAsync(found, isChecked);
    }

    public async Task<bool> GetCheckedAsync()
    {
        var found = await ResolveAsync();
        // UIA TogglePattern から取得
        if (found.UiaElement?.TryGetTogglePattern(out var toggle) == true)
            return toggle.ToggleState == ToggleState.On;
        throw new NotSupportedException("チェック状態を取得できません");
    }

    public async Task<bool> IsEnabledAsync()
    {
        var found = await ResolveAsync();
        return found.UiaElement?.Properties.IsEnabled.ValueOrDefault ?? true;
    }

    public async Task<bool> IsVisibleAsync()
    {
        try
        {
            var found = await _locator.FindAsync(_strategies,
                TimeSpan.FromMilliseconds(500));
            return found.UiaElement?.Properties.IsOffscreen.ValueOrDefault == false;
        }
        catch (ElementNotFoundException)
        {
            return false;
        }
    }

    public IElementAssertions Should() => new ElementAssertions(this);

    // ... DoubleClickAsync, RightClickAsync, WaitUntilExistsAsync 等は同パターン

    private bool IsStillValid(FoundElement element)
    {
        if (element.UiaElement == null) return false;
        try
        {
            // UIA要素がまだ生きているかチェック
            _ = element.UiaElement.Properties.ProcessId.Value;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

### 3.4 FormPage — Page Object 基底クラス

```csharp
/// <summary>
/// Page Object パターンの基底クラス。
/// 各フォームの Page Object はこのクラスを継承する。
/// </summary>
public abstract class FormPage
{
    private readonly HybridElementLocator _locator;
    private readonly ActionExecutor _executor;
    private readonly TimeSpan _defaultTimeout;

    protected FormPage(
        HybridElementLocator locator,
        ActionExecutor executor,
        TimeSpan? defaultTimeout = null)
    {
        _locator = locator;
        _executor = executor;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// ストラテジーチェーンで要素を定義する。
    /// 優先順位の高いストラテジーから順に指定する。
    /// </summary>
    protected IElement Element(params ElementStrategy[] strategies)
    {
        return new Element(_locator, _executor, strategies, _defaultTimeout);
    }

    /// <summary>タイムアウトをカスタマイズした要素</summary>
    protected IElement Element(TimeSpan timeout, params ElementStrategy[] strategies)
    {
        return new Element(_locator, _executor, strategies, timeout);
    }

    /// <summary>フォームが表示されるまで待機</summary>
    public virtual async Task WaitForLoadAsync(TimeSpan? timeout = null)
    {
        // サブクラスでオーバーライドして特定要素の出現を待つことを推奨
        await Task.Delay(200);
    }
}
```

### 3.5 Page Object の使用例

```csharp
// AI が生成する Page Object
public class SearchFormPage : FormPage
{
    public SearchFormPage(HybridElementLocator locator, ActionExecutor executor)
        : base(locator, executor) { }

    public IElement SearchField => Element(
        Strategy.ByAutomationId("cmbSearchField"),
        Strategy.ByName("検索項目", "ComboBox"));

    public IElement SearchCondition => Element(
        Strategy.ByAutomationId("txtSearchCondition"),
        Strategy.ByName("検索条件"),
        Strategy.ByImage("ref/search_textbox.png"));

    public IElement PartialMatch => Element(
        Strategy.ByAutomationId("chkPartialMatch"),
        Strategy.ByName("部分一致"));

    public IElement SearchButton => Element(
        Strategy.ByAutomationId("btnSearch"),
        Strategy.ByName("検索"),
        Strategy.ByImage("ref/search_button.png"));

    public IElement ResultGrid => Element(
        Strategy.ByAutomationId("dgvResults"),
        Strategy.ByControlType("DataGrid"));

    public IElement ResultCount => Element(
        Strategy.ByAutomationId("lblCount"),
        Strategy.ByPattern(@"\d+件"));

    // 意図的に Name なしの閉じるボタン — ByName で Text を使う
    public IElement CloseButton => Element(
        Strategy.ByName("閉じる", "Button"),
        Strategy.ByImage("ref/close_button.png"));

    public override async Task WaitForLoadAsync(TimeSpan? timeout = null)
    {
        await SearchCondition.WaitUntilExistsAsync(timeout);
    }
}
```

---

## 4. Assertions（検証API）

### 4.1 IElementAssertions

```csharp
public interface IElementAssertions
{
    /// <summary>テキストが一致</summary>
    Task HaveTextAsync(string expected);

    /// <summary>テキストを含む</summary>
    Task ContainTextAsync(string expected);

    /// <summary>テキストが正規表現にマッチ</summary>
    Task MatchTextAsync(string regexPattern);

    /// <summary>要素が存在する</summary>
    Task ExistAsync();

    /// <summary>要素が存在しない</summary>
    Task NotExistAsync(TimeSpan? timeout = null);

    /// <summary>有効状態</summary>
    Task BeEnabledAsync();

    /// <summary>無効状態</summary>
    Task BeDisabledAsync();

    /// <summary>表示状態</summary>
    Task BeVisibleAsync();

    /// <summary>非表示状態</summary>
    Task BeHiddenAsync();

    /// <summary>チェック状態</summary>
    Task BeCheckedAsync();

    /// <summary>未チェック状態</summary>
    Task BeUncheckedAsync();
}
```

### 4.2 ElementAssertions 実装

```csharp
internal class ElementAssertions : IElementAssertions
{
    private readonly Element _element;

    internal ElementAssertions(Element element) => _element = element;

    public async Task HaveTextAsync(string expected)
    {
        var actual = await _element.GetTextAsync();
        if (actual != expected)
        {
            throw new AssertionException(
                $"テキストが一致しません。\n  期待: \"{expected}\"\n  実際: \"{actual}\"");
        }
    }

    public async Task ContainTextAsync(string expected)
    {
        var actual = await _element.GetTextAsync();
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new AssertionException(
                $"テキストを含みません。\n  期待（部分一致）: \"{expected}\"\n  実際: \"{actual}\"");
        }
    }

    public async Task ExistAsync()
    {
        try
        {
            await _element.ResolveAsync();
        }
        catch (ElementNotFoundException ex)
        {
            throw new AssertionException($"要素が見つかりません。\n{ex.Message}");
        }
    }

    public async Task NotExistAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (!await _element.IsVisibleAsync())
                return;
            await Task.Delay(200);
        }
        throw new AssertionException("要素がまだ存在しています。");
    }

    // ... BeEnabledAsync, BeDisabledAsync, BeVisibleAsync 等は同パターン
}
```

### 4.3 DataGridView 専用アサーション

DataGridView は行列構造のため専用のアサーションを提供。

```csharp
public interface IGridAssertions
{
    /// <summary>行数の検証</summary>
    Task HaveRowCountAsync(int expected);

    /// <summary>指定列にテキストを含む行が存在</summary>
    Task ContainRowWithTextAsync(string columnName, string text);

    /// <summary>指定列の値一覧を取得</summary>
    Task<IReadOnlyList<string>> GetColumnValuesAsync(string columnName);
}

/// <summary>DataGridView 操作の拡張</summary>
public class GridElement : IElement
{
    private readonly Element _baseElement;

    /// <summary>セル(行, 列)を取得</summary>
    public IElement Cell(int row, int column) { ... }

    /// <summary>行を選択</summary>
    public async Task SelectRowAsync(int rowIndex) { ... }

    /// <summary>ヘッダーをクリック（ソート）</summary>
    public async Task ClickHeaderAsync(string columnName) { ... }

    public IGridAssertions Should() => new GridAssertions(this);
}
```

---

## 5. AppInstance — アプリケーションライフサイクル

### 5.1 LaunchConfig

```csharp
public class LaunchConfig
{
    /// <summary>対象アプリの実行ファイルパス</summary>
    public required string ExePath { get; init; }

    /// <summary>コマンドライン引数</summary>
    public string? Arguments { get; init; }

    /// <summary>作業ディレクトリ</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>アプリ内ロガーのIPC接続を有効化</summary>
    public bool AttachLogger { get; init; }

    /// <summary>テスト失敗時のスクリーンショット自動撮影</summary>
    public bool ScreenshotOnFailure { get; init; } = true;

    /// <summary>スクリーンショット保存ディレクトリ</summary>
    public string ScreenshotDirectory { get; init; } = "test-screenshots";

    /// <summary>要素検索のデフォルトタイムアウト</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>メインウィンドウ出現の待機タイムアウト</summary>
    public TimeSpan LaunchTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>画像認識の実装（null=NullImageMatcher）</summary>
    public IImageMatcher? ImageMatcher { get; init; }

    /// <summary>ビルド構成（E2ETest or Release）</summary>
    public string BuildConfiguration { get; init; } = "E2ETest";
}
```

### 5.2 AppInstance

```csharp
/// <summary>テスト対象アプリケーションのインスタンスを管理</summary>
public class AppInstance : IAsyncDisposable
{
    private Process? _process;
    private IntPtr _mainWindowHandle;
    private UIAutomationDriver? _uiaDriver;
    private ImageRecognitionDriver? _imageDriver;
    private HybridElementLocator? _locator;
    private ActionExecutor? _executor;
    private ScreenCapturer? _capturer;
    private readonly LaunchConfig _config;

    private AppInstance(LaunchConfig config) => _config = config;

    /// <summary>アプリを起動してインスタンスを取得</summary>
    public static async Task<AppInstance> LaunchAsync(LaunchConfig config)
    {
        var instance = new AppInstance(config);
        await instance.StartAsync();
        return instance;
    }

    private async Task StartAsync()
    {
        // 1. プロセス起動
        var psi = new ProcessStartInfo
        {
            FileName = _config.ExePath,
            Arguments = _config.Arguments ?? "",
            WorkingDirectory = _config.WorkingDirectory
                ?? Path.GetDirectoryName(_config.ExePath),
            UseShellExecute = false,
        };
        _process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"アプリの起動に失敗: {_config.ExePath}");

        // 2. メインウィンドウの出現を待機
        _mainWindowHandle = await WaitForMainWindowAsync(_config.LaunchTimeout);

        // 3. ドライバー初期化
        _uiaDriver = new UIAutomationDriver(_mainWindowHandle);

        var imageMatcher = _config.ImageMatcher ?? new NullImageMatcher();
        _capturer = new ScreenCapturer(
            _mainWindowHandle,
            new CaptureOptions { Quality = CaptureQuality.Medium });
        _imageDriver = new ImageRecognitionDriver(_capturer, imageMatcher);

        var drivers = new List<IElementDriver> { _uiaDriver, _imageDriver };
        _locator = new HybridElementLocator(
            drivers, _capturer, _config.DefaultTimeout);
        _executor = new ActionExecutor(_uiaDriver);
    }

    /// <summary>Page Object を取得</summary>
    public async Task<T> GetFormAsync<T>() where T : FormPage
    {
        var page = (T)Activator.CreateInstance(typeof(T), _locator, _executor)!;
        await page.WaitForLoadAsync();
        return page;
    }

    /// <summary>新しいフォーム（モーダルダイアログ等）の出現を待機して取得</summary>
    public async Task<T> WaitForFormAsync<T>(TimeSpan? timeout = null) where T : FormPage
    {
        var effectiveTimeout = timeout ?? _config.DefaultTimeout;
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var page = await GetFormAsync<T>();
                return page;
            }
            catch (ElementNotFoundException)
            {
                await Task.Delay(200);
            }
        }

        throw new TimeoutException(
            $"フォーム {typeof(T).Name} が {effectiveTimeout.TotalSeconds}秒以内に表示されませんでした");
    }

    /// <summary>テスト失敗時の証跡を取得</summary>
    public async Task<FailureReport> CaptureFailureReportAsync()
    {
        var report = new FailureReport
        {
            Timestamp = DateTimeOffset.UtcNow,
        };

        // スクリーンショット
        if (_capturer != null)
        {
            var capture = _capturer.Capture("test_failure");
            var dir = _config.ScreenshotDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir,
                $"failure_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            capture.Bitmap.Save(path);
            report.ScreenshotPath = path;
            capture.Bitmap.Dispose();
        }

        // UIAツリーのダンプ
        if (_uiaDriver != null)
        {
            report.UiaTreeJson = _uiaDriver.DumpTreeJson(_mainWindowHandle);
        }

        return report;
    }

    /// <summary>アプリを閉じる</summary>
    public async Task CloseAsync()
    {
        if (_process != null && !_process.HasExited)
        {
            _process.CloseMainWindow();
            if (!_process.WaitForExit(5000))
            {
                _process.Kill();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _uiaDriver?.Dispose();
        _imageDriver?.Dispose();
        _capturer?.Dispose();
        await CloseAsync();
        _process?.Dispose();
    }
}
```

### 5.3 FailureReport

```csharp
public class FailureReport
{
    public DateTimeOffset Timestamp { get; set; }
    public string? ScreenshotPath { get; set; }
    public string? UiaTreeJson { get; set; }
    public IReadOnlyList<string>? AppLogEntries { get; set; }
}
```

---

## 6. テストベースクラス

### 6.1 WinFormsTestBase（NUnit版）

```csharp
/// <summary>
/// WinForms E2E テストの基底クラス。
/// NUnit の [TestFixture] に継承して使う。
/// </summary>
public abstract class WinFormsTestBase
{
    protected AppInstance App { get; private set; } = null!;

    /// <summary>サブクラスがオーバーライドして LaunchConfig を返す</summary>
    protected abstract LaunchConfig CreateLaunchConfig();

    [OneTimeSetUp]
    public async Task BaseOneTimeSetUp()
    {
        var config = CreateLaunchConfig();
        App = await AppInstance.LaunchAsync(config);
    }

    [TearDown]
    public async Task BaseTearDown()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            var report = await App.CaptureFailureReportAsync();
            TestContext.AddTestAttachment(
                report.ScreenshotPath ?? "", "失敗時スクリーンショット");
        }
    }

    [OneTimeTearDown]
    public async Task BaseOneTimeTearDown()
    {
        await App.DisposeAsync();
    }
}
```

### 6.2 テストコード使用例（AI生成想定）

```csharp
[TestFixture]
public class CustomerSearchTest : WinFormsTestBase
{
    protected override LaunchConfig CreateLaunchConfig() => new()
    {
        ExePath = @"C:\path\to\SampleApp.exe",
        BuildConfiguration = "E2ETest",
    };

    [Test]
    public async Task 顧客検索_田中で検索_1件表示される()
    {
        // Arrange
        var mainForm = await App.GetFormAsync<MainFormPage>();

        // Act — 検索フォームを開く
        await mainForm.CustomerSearchButton.ClickAsync();

        var searchForm = await App.WaitForFormAsync<SearchFormPage>();
        await searchForm.SearchCondition.SetTextAsync("田中");
        await searchForm.SearchButton.ClickAsync();

        // Assert
        await searchForm.ResultCount.Should().HaveTextAsync("1件");
        await searchForm.ResultGrid.Should().ContainTextAsync("田中太郎");
    }

    [Test]
    public async Task 新規顧客追加_保存後一覧に表示される()
    {
        var mainForm = await App.GetFormAsync<MainFormPage>();

        await mainForm.CustomerAddButton.ClickAsync();

        var editForm = await App.WaitForFormAsync<CustomerEditFormPage>();
        await editForm.Name.SetTextAsync("テスト太郎");
        await editForm.Phone.SetTextAsync("090-1234-5678");
        await editForm.Email.SetTextAsync("test@example.com");
        await editForm.Category.SelectAsync("個人");
        await editForm.IsActive.SetCheckedAsync(true);
        await editForm.SaveButton.ClickAsync();

        // モーダルが閉じたことを確認
        await editForm.SaveButton.Should().NotExistAsync();

        // 一覧に追加されたことを確認
        await mainForm.CustomerGrid.Should().ContainTextAsync("テスト太郎");
    }
}
```

---

## 7. エラーハンドリングと診断

### 7.1 例外階層

```
WinFormsTestHarnessException           — 基底例外
  ├── ElementNotFoundException         — 要素が見つからない
  │     └── AllStrategiesFailedException — 全ストラテジー失敗（詳細付き）
  ├── ActionFailedException            — 操作の実行に失敗
  ├── AssertionException               — 検証失敗
  ├── AppLaunchException               — アプリの起動に失敗
  └── TimeoutException                 — 待機タイムアウト
```

### 7.2 ElementNotFoundException の詳細情報

```csharp
public class ElementNotFoundException : WinFormsTestHarnessException
{
    /// <summary>試行したストラテジーと各失敗理由</summary>
    public IReadOnlyList<StrategyFailure> Failures { get; }

    /// <summary>失敗時のスクリーンショットパス</summary>
    public string? ScreenshotPath { get; }

    public override string Message
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine("UI要素が見つかりませんでした。");
            sb.AppendLine("試行したストラテジー:");
            foreach (var f in Failures)
            {
                sb.AppendLine($"  [{f.Strategy.Kind}] {f.Strategy.Description} → {f.Reason}");
            }
            if (ScreenshotPath != null)
                sb.AppendLine($"スクリーンショット: {ScreenshotPath}");
            return sb.ToString();
        }
    }
}

public record StrategyFailure(ElementStrategy Strategy, string Reason);
```

### 7.3 リトライとタイムアウト

```
要素検索のリトライ戦略:
  ストラテジーごとに割り当て時間内でポーリング（200ms間隔）
  全ストラテジー合計でデフォルト10秒

  例: 3つのストラテジーがある場合
    ByAutomationId: 最大 ~3.3秒 ポーリング
    ByName:         最大 ~3.3秒 ポーリング
    ByImage:        最大 ~3.3秒 ポーリング

  ※ 前のストラテジーが即座に失敗すれば、残りの時間は後のストラテジーに配分

操作のリトライ:
  操作自体はリトライしない（べき等性が保証できない）
  操作対象の要素検索にリトライが含まれるため、
  UIの遷移待ちは要素検索のポーリングで吸収される
```

---

## 8. 設定

### 8.1 TestHarnessConfig

テスト全体の設定を環境変数または設定ファイルで制御。

```csharp
public class TestHarnessConfig
{
    /// <summary>要素検索のデフォルトタイムアウト</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>操作後の暗黙の待機時間</summary>
    public TimeSpan ImplicitWait { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>スクリーンショット保存ディレクトリ</summary>
    public string ScreenshotDir { get; set; } = "test-screenshots";

    /// <summary>リファレンス画像ディレクトリ</summary>
    public string ReferenceImageDir { get; set; } = "ref";

    /// <summary>CI環境かどうか（環境変数 CI=true で自動判定）</summary>
    public bool IsCI { get; set; } =
        Environment.GetEnvironmentVariable("CI") == "true";

    /// <summary>CI環境でのタイムアウト倍率</summary>
    public double CITimeoutMultiplier { get; set; } = 2.0;

    /// <summary>有効タイムアウト（CI環境では倍率適用）</summary>
    public TimeSpan EffectiveTimeout =>
        IsCI
            ? TimeSpan.FromMilliseconds(DefaultTimeout.TotalMilliseconds * CITimeoutMultiplier)
            : DefaultTimeout;
}
```

### 8.2 環境変数

```
WFTH_TIMEOUT=15000          — デフォルトタイムアウト (ms)
WFTH_SCREENSHOT_DIR=./ss    — スクリーンショット保存先
WFTH_REF_IMAGE_DIR=./ref    — リファレンス画像ディレクトリ
CI=true                     — CI環境フラグ
E2E_TIMEOUT_MULTIPLIER=2.5  — CI環境タイムアウト倍率
```

---

## 9. プロジェクト構成

### 9.1 ディレクトリ構成

```
src/WinFormsTestHarness.Core/
├── WinFormsTestHarness.Core.csproj
├── Driver/
│   ├── IElementDriver.cs              — 要素特定ドライバーIF
│   ├── UIAutomationDriver.cs          — FlaUI.UIA3 実装
│   ├── ImageRecognitionDriver.cs      — 画像認識実装
│   ├── IImageMatcher.cs               — 画像認識抽象化
│   ├── NullImageMatcher.cs            — スタブ実装
│   ├── HybridElementLocator.cs        — フォールバックチェーン
│   └── ActionExecutor.cs              — UI操作実行
├── Abstraction/
│   ├── IElement.cs                    — 要素操作IF
│   ├── Element.cs                     — IElement 実装
│   ├── FormPage.cs                    — Page Object 基底
│   ├── GridElement.cs                 — DataGridView 専用
│   ├── ElementStrategy.cs            — ストラテジー定義
│   └── Strategy.cs                   — ストラテジー生成ヘルパー
├── Assertions/
│   ├── IElementAssertions.cs          — 検証IF
│   ├── ElementAssertions.cs           — 検証実装
│   └── IGridAssertions.cs             — DataGridView 検証IF
├── App/
│   ├── AppInstance.cs                 — アプリライフサイクル管理
│   ├── LaunchConfig.cs                — 起動設定
│   └── FailureReport.cs              — 失敗時証跡
├── Infrastructure/
│   ├── MouseHelper.cs                 — Win32 マウス操作
│   ├── NativeMethods.cs               — P/Invoke 定義
│   └── TestHarnessConfig.cs           — 全体設定
├── Testing/
│   └── WinFormsTestBase.cs            — NUnit テスト基底
└── Exceptions/
    ├── WinFormsTestHarnessException.cs
    ├── ElementNotFoundException.cs
    ├── ActionFailedException.cs
    └── AssertionException.cs
```

### 9.2 csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>  <!-- System.Windows.Point 等のために必要 -->
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FlaUI.UIA3" Version="4.*" />
    <PackageReference Include="NUnit" Version="4.*" />
    <ProjectReference Include="..\WinFormsTestHarness.Capture\WinFormsTestHarness.Capture.csproj" />
  </ItemGroup>
</Project>
```

---

## 10. 実装優先度

| 機能 | MVP段階 | 理由 |
|------|---------|------|
| UIAutomationDriver | **MVP E** | コア機能、UIA特定が主要パス |
| HybridElementLocator | **MVP E** | ストラテジーチェーンの基盤 |
| ActionExecutor (Click, SetText) | **MVP E** | 基本操作 |
| IElement / Element | **MVP E** | テストコードのインターフェース |
| FormPage | **MVP E** | Page Object の基盤 |
| Strategy (ByAutomationId, ByName) | **MVP E** | UIA系ストラテジー |
| WinFormsTestBase | **MVP E** | テスト実行の基盤 |
| AppInstance | **MVP E** | アプリライフサイクル |
| ElementAssertions (HaveText, Exist) | **MVP E** | 基本検証 |
| ElementNotFoundException 詳細 | **MVP E** | デバッグに必須 |
| NullImageMatcher | **MVP E** | 画像認識なしでも動作 |
| ActionExecutor (Select, SetChecked) | MVP E+ | 追加操作 |
| GridElement / IGridAssertions | MVP E+ | DataGridView対応 |
| ImageRecognitionDriver | 将来 | 画像認識バックエンド次第 |
| Strategy (ByImage, ByPattern) | 将来 | 画像認識と連動 |
| TestHarnessConfig 環境変数 | MVP E+ | CI対応 |

---

## 11. 設計上のトレードオフと決定事項

### 11.1 FlaUI vs System.Windows.Automation

wfth-inspect では両方をサポートしているが、Core では **FlaUI.UIA3 のみ** とする。

理由:
- FlaUI はパターン操作（InvokePattern, ValuePattern 等）の API が使いやすい
- System.Windows.Automation は .NET 8 での利用にFrameworkReference が必要で煩雑
- テスト実行では操作も行うため、API の使いやすさが重要
- wfth-inspect の比較結果で問題が見つかれば再検討

### 11.2 テストランナー依存

NUnit をプライマリサポートとし、WinFormsTestBase は NUnit の属性に依存する。xUnit / MSTest 版は将来必要に応じて追加。

理由:
- 1つのランナーに集中してAPIを磨く
- NUnit は [TestCaseSource] で仕様書のテストケースIDと紐付けしやすい
- 基盤部分（AppInstance, HybridElementLocator 等）はランナー非依存

### 11.3 同期 vs 非同期

全ての公開APIを `async Task` で統一。

理由:
- UI操作は本質的に非同期（ポーリング待機、画面遷移待ち）
- 同期版を提供すると `.Result` や `.Wait()` によるデッドロックリスク
- テストコードは async/await で記述する想定

### 11.4 画像認識の初期戦略

MVP E では `NullImageMatcher`（常に null）を提供し、画像認識は「使えないがエラーにならない」状態にする。画像認識バックエンドの選定は別途検討。

候補:
- **OpenCvSharp**: テンプレートマッチングの定番。精度高い。NuGet で利用可能
- **Windows.Media.Ocr**: Windows 10+ 標準OCR。追加インストール不要
- **Tesseract**: OSS OCR。多言語対応。セットアップが手間
- **LLM Vision API**: 最も柔軟。コスト高。ネットワーク依存

### 11.5 要素キャッシュの有効期間

Element は見つけた UIA 要素をキャッシュするが、以下のタイミングで無効化する:
- クリック操作後（画面遷移の可能性）
- 明示的な再検索要求
- キャッシュされた UIA 要素のプロパティ取得が例外を投げた場合

### 11.6 WaitForFormAsync のフォーム判定

新しいフォーム（モーダルダイアログ等）の出現をどう判定するか:

```
方式: FormPage.WaitForLoadAsync() の成功をフォーム出現の判定とする

各 Page Object が WaitForLoadAsync をオーバーライドして、
その画面固有の要素（タイトルバー、特定コントロール等）の出現を待つ。

メリット: フォーム判定ロジックがPage Objectに集約される
デメリット: WaitForLoadAsync の実装が Page Object ごとに必要
```
