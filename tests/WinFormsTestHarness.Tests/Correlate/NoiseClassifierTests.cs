using NUnit.Framework;
using WinFormsTestHarness.Correlate.Correlation;
using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Tests.Correlate;

[TestFixture]
public class NoiseClassifierTests
{
    private NoiseClassifier _classifier = null!;

    [SetUp]
    public void SetUp()
    {
        _classifier = new NoiseClassifier(0.7);
    }

    [Test]
    public void empty_click_UIA変化なしAppLogなしのClickはノイズ判定される()
    {
        var action = new AggregatedAction { Type = "Click", Ts = "2026-01-01T00:00:01Z", Rx = 100, Ry = 100 };
        var emptyDiff = new UiaDiff();

        var result = _classifier.Classify(action, emptyDiff, null, null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Reason, Is.EqualTo("empty_click"));
        Assert.That(result.Confidence, Is.EqualTo(0.8));
    }

    [Test]
    public void duplicate_click_同一座標500ms以内のClickはノイズ判定される()
    {
        var prev = new AggregatedAction { Type = "Click", Ts = "2026-01-01T00:00:01.000Z", Rx = 100, Ry = 100 };
        var action = new AggregatedAction { Type = "Click", Ts = "2026-01-01T00:00:01.300Z", Rx = 100, Ry = 100 };
        var emptyDiff = new UiaDiff();

        var result = _classifier.Classify(action, emptyDiff, null, prev);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Reason, Is.EqualTo("duplicate_click"));
        Assert.That(result.Confidence, Is.EqualTo(0.9));
    }

    [Test]
    public void accidental_drag_移動距離5px未満のDragAndDropはノイズ判定される()
    {
        var action = new AggregatedAction
        {
            Type = "DragAndDrop",
            Ts = "2026-01-01T00:00:01Z",
            Rx = 100, Ry = 100,
            EndRx = 102, EndRy = 103
        };

        var result = _classifier.Classify(action, null, null, null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Reason, Is.EqualTo("accidental_drag"));
        Assert.That(result.Confidence, Is.EqualTo(0.85));
    }

    [Test]
    public void UIA変化ありのClickはノイズ判定されない()
    {
        var action = new AggregatedAction { Type = "Click", Ts = "2026-01-01T00:00:01Z", Rx = 100, Ry = 100 };
        var diff = new UiaDiff
        {
            Added = { new UiaDiffEntry { AutomationId = "newBtn", ControlType = "Button" } }
        };

        var result = _classifier.Classify(action, diff, null, null);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void TextInputはノイズ判定されない()
    {
        var action = new AggregatedAction { Type = "TextInput", Ts = "2026-01-01T00:00:01Z", Text = "hello" };

        var result = _classifier.Classify(action, null, null, null);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void 閾値未満のconfidenceはnullを返す()
    {
        var highThreshold = new NoiseClassifier(0.95);
        var action = new AggregatedAction { Type = "Click", Ts = "2026-01-01T00:00:01Z", Rx = 100, Ry = 100 };
        var emptyDiff = new UiaDiff();

        var result = highThreshold.Classify(action, emptyDiff, null, null);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void 十分な移動距離のDragAndDropはノイズ判定されない()
    {
        var action = new AggregatedAction
        {
            Type = "DragAndDrop",
            Ts = "2026-01-01T00:00:01Z",
            Rx = 100, Ry = 100,
            EndRx = 200, EndRy = 200
        };

        var result = _classifier.Classify(action, null, null, null);

        Assert.That(result, Is.Null);
    }
}
