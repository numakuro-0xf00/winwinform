using NUnit.Framework;
using WinFormsTestHarness.Aggregate.Aggregation;
using WinFormsTestHarness.Aggregate.Models;

namespace WinFormsTestHarness.Tests.Aggregate;

[TestFixture]
public class KeySequenceAggregatorTests
{
    private KeySequenceAggregator _aggregator = null!;

    [SetUp]
    public void SetUp()
    {
        _aggregator = new KeySequenceAggregator(textTimeoutMs: 500);
    }

    private static string Ts(int ms) => $"2026-02-23T10:00:00.{ms:D3}Z";

    private static RawKeyEvent Key(string action, int ms, string key, string? ch = null, int vk = 0, string? modifier = null) => new()
    {
        Ts = Ts(ms),
        Type = "key",
        Action = action,
        Key = key,
        Char = ch,
        Vk = vk,
        Modifier = modifier,
    };

    [Test]
    public void abc連続入力_500ms無入力後_TextInputが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "A", "a")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", 50, "B", "b")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", 100, "C", "c")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("TextInput"));
        Assert.That(results[0].Text, Is.EqualTo("abc"));
        Assert.That(results[0].StartTs, Is.EqualTo(Ts(0)));
        Assert.That(results[0].EndTs, Is.EqualTo(Ts(100)));
    }

    [Test]
    public void ab入力後Enter_TextInputとSpecialKeyが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "A", "a")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", 50, "B", "b")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", 100, "Enter")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Type, Is.EqualTo("TextInput"));
        Assert.That(results[0].Text, Is.EqualTo("ab"));
        Assert.That(results[1].Type, Is.EqualTo("SpecialKey"));
        Assert.That(results[1].Key, Is.EqualTo("Enter"));
    }

    [Test]
    public void Shift_T_修飾キーは含まれずTextInputが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "LShift")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", 10, "T", "T")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("TextInput"));
        Assert.That(results[0].Text, Is.EqualTo("T"));
    }

    [Test]
    public void KeyUpイベントは無視される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("up", 0, "A", "a")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void テキスト入力タイムアウトで分割される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "A", "a")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", 50, "B", "b")));
        // 600ms 後の次のキー（500ms timeout 超過）
        results.AddRange(_aggregator.ProcessEvent(Key("down", 650, "C", "c")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Type, Is.EqualTo("TextInput"));
        Assert.That(results[0].Text, Is.EqualTo("ab"));
        Assert.That(results[1].Type, Is.EqualTo("TextInput"));
        Assert.That(results[1].Text, Is.EqualTo("c"));
    }

    [Test]
    public void Tab_SpecialKeyが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "Tab")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("SpecialKey"));
        Assert.That(results[0].Key, Is.EqualTo("Tab"));
    }

    [Test]
    public void Escape_SpecialKeyが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "Escape")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("SpecialKey"));
        Assert.That(results[0].Key, Is.EqualTo("Escape"));
    }

    [Test]
    public void F1_SpecialKeyが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "F1")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("SpecialKey"));
        Assert.That(results[0].Key, Is.EqualTo("F1"));
    }

    [Test]
    public void 座標コンテキストがTextInputに付与される()
    {
        _aggregator.SetCoordinateContext(450, 320, 230, 180);

        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "A", "a")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Sx, Is.EqualTo(450));
        Assert.That(results[0].Sy, Is.EqualTo(320));
        Assert.That(results[0].Rx, Is.EqualTo(230));
        Assert.That(results[0].Ry, Is.EqualTo(180));
    }

    [Test]
    public void 座標コンテキストがSpecialKeyに付与される()
    {
        _aggregator.SetCoordinateContext(450, 320, 230, 180);

        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "Enter")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Sx, Is.EqualTo(450));
        Assert.That(results[0].Ry, Is.EqualTo(180));
    }

    [Test]
    public void Charなしのキーイベントはテキストバッファに追加されない()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "Control")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", 10, "A", null))); // Ctrl+A (char なし)
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void Delete_Backspace_SpecialKeyが生成される()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "Delete")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", 100, "Backspace")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Key, Is.EqualTo("Delete"));
        Assert.That(results[1].Key, Is.EqualTo("Backspace"));
    }
}
