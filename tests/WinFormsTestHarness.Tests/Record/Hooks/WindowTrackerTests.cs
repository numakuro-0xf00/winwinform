using NUnit.Framework;
using WinFormsTestHarness.Record.Hooks;
using WinFormsTestHarness.Tests.Record.Fakes;

namespace WinFormsTestHarness.Tests.Record.Hooks;

[TestFixture]
public class WindowTrackerTests
{
    private FakeWindowApi _api = null!;
    private WindowTracker _tracker = null!;
    private static readonly IntPtr MainHwnd = new(0x1000);
    private const uint MainPid = 100;

    [SetUp]
    public void SetUp()
    {
        _api = new FakeWindowApi();
        _api.AddWindow(MainHwnd, new FakeWindowApi.WindowState
        {
            ProcessId = MainPid,
            Title = "メインウィンドウ",
            Left = 100, Top = 100, Width = 800, Height = 600,
            RootOwner = MainHwnd,
        });
        _tracker = new WindowTracker(_api, MainHwnd, MainPid);
    }

    [Test]
    public void BelongsToTarget_メインウィンドウはtrue()
    {
        Assert.That(_tracker.BelongsToTarget(MainHwnd), Is.True);
    }

    [Test]
    public void BelongsToTarget_同一プロセスの別ウィンドウはtrue()
    {
        var childHwnd = new IntPtr(0x2000);
        _api.AddWindow(childHwnd, new FakeWindowApi.WindowState
        {
            ProcessId = MainPid,
            Title = "子ウィンドウ",
            RootOwner = childHwnd,
        });

        Assert.That(_tracker.BelongsToTarget(childHwnd), Is.True);
    }

    [Test]
    public void BelongsToTarget_別プロセスのウィンドウはfalse()
    {
        var otherHwnd = new IntPtr(0x3000);
        _api.AddWindow(otherHwnd, new FakeWindowApi.WindowState
        {
            ProcessId = 999,
            Title = "他アプリ",
            RootOwner = otherHwnd,
        });

        Assert.That(_tracker.BelongsToTarget(otherHwnd), Is.False);
    }

    [Test]
    public void BelongsToTarget_モーダルダイアログはオーナー経由でtrue()
    {
        var dialogHwnd = new IntPtr(0x4000);
        _api.AddWindow(dialogHwnd, new FakeWindowApi.WindowState
        {
            ProcessId = 200, // 別プロセスでもオーナーが対象なら
            Title = "ダイアログ",
            RootOwner = MainHwnd, // オーナーがメインウィンドウ
            Style = unchecked((int)0x80000000), // WS_POPUP
        });

        Assert.That(_tracker.BelongsToTarget(dialogHwnd), Is.True);
    }

    [Test]
    public void BelongsToTarget_存在しないウィンドウはfalse()
    {
        Assert.That(_tracker.BelongsToTarget(new IntPtr(0x9999)), Is.False);
    }

    [Test]
    public void IsModalDialog_メインウィンドウはfalse()
    {
        Assert.That(_tracker.IsModalDialog(MainHwnd), Is.False);
    }

    [Test]
    public void IsModalDialog_ポップアップスタイルのウィンドウはtrue()
    {
        var dialogHwnd = new IntPtr(0x5000);
        _api.AddWindow(dialogHwnd, new FakeWindowApi.WindowState
        {
            ProcessId = MainPid,
            Style = unchecked((int)0x80000000), // WS_POPUP
        });

        Assert.That(_tracker.IsModalDialog(dialogHwnd), Is.True);
    }

    [Test]
    public void CreateWindowEvent_ウィンドウ情報を含むイベントを生成()
    {
        var evt = _tracker.CreateWindowEvent(MainHwnd, "focus");

        Assert.That(evt.Action, Is.EqualTo("focus"));
        Assert.That(evt.Title, Is.EqualTo("メインウィンドウ"));
        Assert.That(evt.Rect, Is.Not.Null);
        Assert.That(evt.Rect!.Width, Is.EqualTo(800));
        Assert.That(evt.Rect.Height, Is.EqualTo(600));
    }

    [Test]
    public void TryTrack_新規ウィンドウを追跡に追加()
    {
        var newHwnd = new IntPtr(0x6000);
        Assert.That(_tracker.TryTrack(newHwnd), Is.True);
        Assert.That(_tracker.TrackedCount, Is.EqualTo(2));
    }

    [Test]
    public void TryUntrack_メインウィンドウは除外できない()
    {
        Assert.That(_tracker.TryUntrack(MainHwnd), Is.False);
        Assert.That(_tracker.TrackedCount, Is.EqualTo(1));
    }

    [Test]
    public void BelongsToTarget_同一プロセスの別ウィンドウを追跡に自動追加()
    {
        var childHwnd = new IntPtr(0x2000);
        _api.AddWindow(childHwnd, new FakeWindowApi.WindowState
        {
            ProcessId = MainPid,
            Title = "子ウィンドウ",
            RootOwner = childHwnd,
        });

        Assert.That(_tracker.TrackedCount, Is.EqualTo(1), "判定前は初期ウィンドウのみ");
        _tracker.BelongsToTarget(childHwnd);
        Assert.That(_tracker.TrackedCount, Is.EqualTo(2), "判定後は子ウィンドウも追跡対象に追加される");
    }
}
