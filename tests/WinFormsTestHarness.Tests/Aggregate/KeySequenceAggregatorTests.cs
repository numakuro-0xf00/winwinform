using NUnit.Framework;
using WinFormsTestHarness.Aggregate.Aggregation;
using WinFormsTestHarness.Aggregate.Models;

namespace WinFormsTestHarness.Tests.Aggregate;

[TestFixture]
public class KeySequenceAggregatorTests
{
    private const int TextTimeoutMs = 500;

    private KeySequenceAggregator _aggregator = null!;

    [SetUp]
    public void SetUp()
    {
        _aggregator = new KeySequenceAggregator(textTimeoutMs: TextTimeoutMs);
    }

    private static string Ts(int ms)
    {
        var dt = new DateTimeOffset(2026, 2, 23, 10, 0, 0, TimeSpan.Zero).AddMilliseconds(ms);
        return dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }

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
        Assert.Multiple(() =>
        {
            Assert.That(results[0].Type, Is.EqualTo("TextInput"));
            Assert.That(results[0].Text, Is.EqualTo("abc"));
            Assert.That(results[0].StartTs, Is.EqualTo(Ts(0)));
            Assert.That(results[0].EndTs, Is.EqualTo(Ts(100)));
        });
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
    public void テキスト入力_ちょうどTextTimeout_分割されない()
    {
        // 境界値: ちょうど TextTimeoutMs (500ms) → 分割されない（> 判定）
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "A", "a")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", TextTimeoutMs, "B", "b")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Text, Is.EqualTo("ab"));
    }

    [Test]
    public void テキスト入力_TextTimeoutを1ms超過_分割される()
    {
        // 境界値: TextTimeoutMs + 1 (501ms) → 分割される（> 判定）
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "A", "a")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", TextTimeoutMs + 1, "B", "b")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].Text, Is.EqualTo("a"));
        Assert.That(results[1].Text, Is.EqualTo("b"));
    }

    [TestCase("Tab")]
    [TestCase("Escape")]
    [TestCase("F1")]
    [TestCase("Delete")]
    [TestCase("Backspace")]
    [TestCase("Home")]
    [TestCase("PageUp")]
    public void SpecialKeyが押されたときSpecialKeyアクションが生成される(string key)
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, key)));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Type, Is.EqualTo("SpecialKey"));
        Assert.That(results[0].Key, Is.EqualTo(key));
    }

    [Test]
    public void 座標コンテキストがTextInputに付与される()
    {
        _aggregator.SetCoordinateContext(450, 320, 230, 180);

        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "A", "a")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(results[0].Sx, Is.EqualTo(450));
            Assert.That(results[0].Sy, Is.EqualTo(320));
            Assert.That(results[0].Rx, Is.EqualTo(230));
            Assert.That(results[0].Ry, Is.EqualTo(180));
        });
    }

    [Test]
    public void 座標コンテキストがSpecialKeyに付与される()
    {
        _aggregator.SetCoordinateContext(450, 320, 230, 180);

        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "Enter")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(results[0].Sx, Is.EqualTo(450));
            Assert.That(results[0].Sy, Is.EqualTo(320));
            Assert.That(results[0].Rx, Is.EqualTo(230));
            Assert.That(results[0].Ry, Is.EqualTo(180));
        });
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
    public void CtrlA後の通常入力がテキストバッファを汚染しない()
    {
        var results = new List<AggregatedAction>();
        results.AddRange(_aggregator.ProcessEvent(Key("down", 0, "Control")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", 10, "A", null))); // Ctrl+A
        results.AddRange(_aggregator.ProcessEvent(Key("down", 100, "H", "h")));
        results.AddRange(_aggregator.ProcessEvent(Key("down", 150, "I", "i")));
        results.AddRange(_aggregator.Flush());

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Text, Is.EqualTo("hi"));
    }
}
