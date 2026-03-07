using NUnit.Framework;
using WinFormsTestHarness.Record.Monitoring;
using WinFormsTestHarness.Tests.Record.Fakes;

namespace WinFormsTestHarness.Tests.Record.Monitoring;

[TestFixture]
public class AppHealthMonitorTests
{
    private FakeAppHealthApi _api = null!;
    private AppHealthMonitor _monitor = null!;

    [SetUp]
    public void SetUp()
    {
        _api = new FakeAppHealthApi();
        _monitor = new AppHealthMonitor(_api, 100, new IntPtr(0x1000));
    }

    [Test]
    public void Check_プロセス存在かつ応答ありはResponsive()
    {
        Assert.That(_monitor.Check(), Is.EqualTo(AppStatus.Responsive));
    }

    [Test]
    public void Check_プロセス終了はExited()
    {
        _api.ProcessAlive = false;
        Assert.That(_monitor.Check(), Is.EqualTo(AppStatus.Exited));
    }

    [Test]
    public void Check_プロセス存在で応答なしはHung()
    {
        _api.WindowResponsive = false;
        Assert.That(_monitor.Check(), Is.EqualTo(AppStatus.Hung));
    }

    [Test]
    public void Check_プロセス終了はHungより優先()
    {
        _api.ProcessAlive = false;
        _api.WindowResponsive = false;
        Assert.That(_monitor.Check(), Is.EqualTo(AppStatus.Exited));
    }
}
