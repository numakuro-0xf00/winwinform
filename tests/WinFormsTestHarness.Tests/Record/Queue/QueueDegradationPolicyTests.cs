using NUnit.Framework;
using WinFormsTestHarness.Record.Events;
using WinFormsTestHarness.Record.Queue;

namespace WinFormsTestHarness.Tests.Record.Queue;

[TestFixture]
public class QueueDegradationPolicyTests
{
    private QueueDegradationPolicy _policy = null!;
    private const int Capacity = 1000;

    [SetUp]
    public void SetUp()
    {
        _policy = new QueueDegradationPolicy(Capacity);
    }

    [Test]
    public void Evaluate_50パーセント未満はNormal()
    {
        Assert.That(_policy.Evaluate(0), Is.EqualTo(DegradationMode.Normal));
        Assert.That(_policy.Evaluate(499), Is.EqualTo(DegradationMode.Normal));
    }

    [Test]
    public void Evaluate_50から75パーセントはDropMouseMove()
    {
        Assert.That(_policy.Evaluate(500), Is.EqualTo(DegradationMode.DropMouseMove));
        Assert.That(_policy.Evaluate(749), Is.EqualTo(DegradationMode.DropMouseMove));
    }

    [Test]
    public void Evaluate_75から90パーセントはDropMouse()
    {
        Assert.That(_policy.Evaluate(750), Is.EqualTo(DegradationMode.DropMouse));
        Assert.That(_policy.Evaluate(899), Is.EqualTo(DegradationMode.DropMouse));
    }

    [Test]
    public void Evaluate_90パーセント以上はCriticalOnly()
    {
        Assert.That(_policy.Evaluate(900), Is.EqualTo(DegradationMode.CriticalOnly));
        Assert.That(_policy.Evaluate(1000), Is.EqualTo(DegradationMode.CriticalOnly));
    }

    [Test]
    public void Classify_MouseMoveイベントはMouseMove()
    {
        var evt = new MouseEvent { Action = "move" };
        Assert.That(QueueDegradationPolicy.Classify(evt), Is.EqualTo(EventCategory.MouseMove));
    }

    [Test]
    public void Classify_MouseClickイベントはMouseAction()
    {
        var evt = new MouseEvent { Action = "click" };
        Assert.That(QueueDegradationPolicy.Classify(evt), Is.EqualTo(EventCategory.MouseAction));
    }

    [Test]
    public void Classify_KeyEventはKey()
    {
        var evt = new KeyEvent { Action = "down" };
        Assert.That(QueueDegradationPolicy.Classify(evt), Is.EqualTo(EventCategory.Key));
    }

    [Test]
    public void Classify_SessionEventはSession()
    {
        var evt = new SessionEvent { Action = "start" };
        Assert.That(QueueDegradationPolicy.Classify(evt), Is.EqualTo(EventCategory.Session));
    }

    [Test]
    public void ShouldAccept_Normal時はすべて受け入れ()
    {
        var move = new MouseEvent { Action = "move" };
        Assert.That(_policy.ShouldAccept(move, 100), Is.True);
    }

    [Test]
    public void ShouldAccept_DropMouseMove時はマウス移動のみ拒否()
    {
        var move = new MouseEvent { Action = "move" };
        var click = new MouseEvent { Action = "click" };
        var key = new KeyEvent { Action = "down" };

        Assert.That(_policy.ShouldAccept(move, 500), Is.False);
        Assert.That(_policy.ShouldAccept(click, 500), Is.True);
        Assert.That(_policy.ShouldAccept(key, 500), Is.True);
    }

    [Test]
    public void ShouldAccept_DropMouse時はマウス全般を拒否()
    {
        var move = new MouseEvent { Action = "move" };
        var click = new MouseEvent { Action = "click" };
        var key = new KeyEvent { Action = "down" };

        Assert.That(_policy.ShouldAccept(move, 800), Is.False);
        Assert.That(_policy.ShouldAccept(click, 800), Is.False);
        Assert.That(_policy.ShouldAccept(key, 800), Is.True);
    }

    [Test]
    public void ShouldAccept_CriticalOnly時はSessionとKeyとWindowのみ受け入れ()
    {
        var session = new SessionEvent { Action = "stop" };
        var key = new KeyEvent { Action = "down" };
        var window = new WindowEvent { Action = "focus" };
        var mouse = new MouseEvent { Action = "click" };
        var system = new SystemEvent { Action = "hook_health" };

        Assert.That(_policy.ShouldAccept(session, 950), Is.True);
        Assert.That(_policy.ShouldAccept(key, 950), Is.True);
        Assert.That(_policy.ShouldAccept(window, 950), Is.True);
        Assert.That(_policy.ShouldAccept(mouse, 950), Is.False);
        Assert.That(_policy.ShouldAccept(system, 950), Is.False);
    }
}
