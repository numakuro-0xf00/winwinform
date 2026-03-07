using NUnit.Framework;
using WinFormsTestHarness.Record.Monitoring;
using WinFormsTestHarness.Tests.Record.Fakes;

namespace WinFormsTestHarness.Tests.Record.Monitoring;

[TestFixture]
public class HookHealthMonitorTests
{
    private FakeSystemClock _clock = null!;
    private FakeProbeInput _probe = null!;
    private HookHealthMonitor _monitor = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeSystemClock();
        _probe = new FakeProbeInput();
        _monitor = new HookHealthMonitor(_clock, _probe, TimeSpan.FromMilliseconds(500));
    }

    [Test]
    public void Check_初期状態はAlive()
    {
        Assert.That(_monitor.Check(), Is.EqualTo(HookStatus.Alive));
    }

    [Test]
    public void Check_アクティビティ後はAlive()
    {
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        _monitor.RecordActivity();
        Assert.That(_monitor.Check(), Is.EqualTo(HookStatus.Alive));
    }

    [Test]
    public void Check_タイムアウト経過でプローブ送信しAliveIdle()
    {
        _clock.Advance(TimeSpan.FromMilliseconds(600));
        var status = _monitor.Check();
        Assert.That(status, Is.EqualTo(HookStatus.AliveIdle));
        Assert.That(_probe.ProbeCount, Is.EqualTo(1));
    }

    [Test]
    public void Check_プローブ応答なしでPossiblyDead()
    {
        // タイムアウト経過でプローブ送信
        _clock.Advance(TimeSpan.FromMilliseconds(600));
        _monitor.Check(); // AliveIdle, probe sent

        // さらにタイムアウト経過
        _clock.Advance(TimeSpan.FromMilliseconds(600));
        var status = _monitor.Check();
        Assert.That(status, Is.EqualTo(HookStatus.PossiblyDead));
    }

    [Test]
    public void Check_プローブ後にRecordActivityでAliveに復帰()
    {
        _clock.Advance(TimeSpan.FromMilliseconds(600));
        _monitor.Check(); // probe sent

        _monitor.RecordActivity(); // probe response
        Assert.That(_monitor.Check(), Is.EqualTo(HookStatus.Alive));
    }

    [Test]
    public void Check_プローブ送信後にタイムアウト未満の再CheckはAliveIdle維持()
    {
        // タイムアウト経過でプローブ送信
        _clock.Advance(TimeSpan.FromMilliseconds(600));
        _monitor.Check(); // AliveIdle, probe sent

        // タイムアウト未満の微小時間だけ経過
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        var status = _monitor.Check();
        Assert.That(status, Is.EqualTo(HookStatus.AliveIdle));
        Assert.That(_probe.ProbeCount, Is.EqualTo(1), "追加プローブは送信されない");
    }
}
