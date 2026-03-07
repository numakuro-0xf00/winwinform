using NUnit.Framework;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.IO;
using WinFormsTestHarness.Common.Serialization;
using WinFormsTestHarness.Correlate.Correlation;
using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Tests.Correlate;

[TestFixture]
public class TimeWindowCorrelatorTests
{
    private DiagnosticContext _diag = null!;

    [SetUp]
    public void SetUp()
    {
        _diag = new DiagnosticContext(false, true);
    }

    private (List<CorrelatedAction> actions, CorrelationSummary? summary) RunCorrelator(
        string inputNdjson,
        List<UiaSnapshot>? uiaSnapshots = null,
        List<AppLogEntry>? appLogEntries = null,
        int windowMs = 2000,
        bool includeNoise = true,
        bool explain = false)
    {
        var correlator = new TimeWindowCorrelator(
            uiaSnapshots ?? new List<UiaSnapshot>(),
            appLogEntries,
            null,
            windowMs,
            includeNoise,
            0.7,
            explain,
            _diag);

        var stdin = new NdJsonReader(new StringReader(inputNdjson));
        var outputWriter = new StringWriter();
        using var stdout = new NdJsonWriter(outputWriter);

        correlator.Execute(stdin, stdout);

        var lines = outputWriter.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var actions = new List<CorrelatedAction>();
        CorrelationSummary? summary = null;

        foreach (var line in lines)
        {
            if (line.Contains("\"type\":\"summary\""))
                summary = JsonHelper.Deserialize<CorrelationSummary>(line);
            else
                actions.Add(JsonHelper.Deserialize<CorrelatedAction>(line)!);
        }

        return (actions, summary);
    }

    [Test]
    public void 窓内のUIA変化が紐付けられる()
    {
        var uiaSnapshots = new List<UiaSnapshot>
        {
            new() { Ts = "2026-01-01T00:00:04Z", AutomationId = "Form", ControlType = "Window",
                Children = new List<UiaNodeModel> { new() { AutomationId = "btn1", Name = "ボタン", ControlType = "Button" } } },
            new() { Ts = "2026-01-01T00:00:06Z", AutomationId = "Form", ControlType = "Window",
                Children = new List<UiaNodeModel>
                {
                    new() { AutomationId = "btn1", Name = "ボタン", ControlType = "Button" },
                    new() { AutomationId = "newElement", Name = "新要素", ControlType = "Text" }
                } }
        };

        var input = "{\"ts\":\"2026-01-01T00:00:05Z\",\"type\":\"Click\",\"button\":\"Left\",\"rx\":100,\"ry\":100}";

        var (actions, summary) = RunCorrelator(input, uiaSnapshots);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].Type, Is.EqualTo("Click"));
        Assert.That(actions[0].UiaDiff, Is.Not.Null);
        Assert.That(actions[0].UiaDiff!.Added, Has.Count.EqualTo(1));
        Assert.That(actions[0].UiaDiff.Added[0].AutomationId, Is.EqualTo("newElement"));
    }

    [Test]
    public void 窓外のUIA変化は紐付けされない()
    {
        var uiaSnapshots = new List<UiaSnapshot>
        {
            new() { Ts = "2026-01-01T00:00:01Z", AutomationId = "Form", ControlType = "Window",
                Children = new List<UiaNodeModel> { new() { AutomationId = "btn1", Name = "ボタン", ControlType = "Button" } } },
            new() { Ts = "2026-01-01T00:00:10Z", AutomationId = "Form", ControlType = "Window",
                Children = new List<UiaNodeModel>
                {
                    new() { AutomationId = "btn1", Name = "ボタン", ControlType = "Button" },
                    new() { AutomationId = "newElement", Name = "新要素", ControlType = "Text" }
                } }
        };

        var input = "{\"ts\":\"2026-01-01T00:00:05Z\",\"type\":\"Click\",\"button\":\"Left\",\"rx\":100,\"ry\":100}";

        var (actions, _) = RunCorrelator(input, uiaSnapshots, windowMs: 2000);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].UiaDiff, Is.Null);
    }

    [Test]
    public void ノイズフィルタがinclude_noise_falseで除外する()
    {
        var input = "{\"ts\":\"2026-01-01T00:00:05Z\",\"type\":\"Click\",\"button\":\"Left\",\"rx\":100,\"ry\":100}";

        var (actions, summary) = RunCorrelator(input, includeNoise: false);

        Assert.That(actions, Is.Empty);
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.Metrics.NoiseActions, Is.EqualTo(1));
    }

    [Test]
    public void ノイズフィルタがinclude_noise_trueでノイズも出力する()
    {
        var input = "{\"ts\":\"2026-01-01T00:00:05Z\",\"type\":\"Click\",\"button\":\"Left\",\"rx\":100,\"ry\":100}";

        var (actions, _) = RunCorrelator(input, includeNoise: true);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].Noise, Is.Not.Null);
        Assert.That(actions[0].Noise!.Reason, Is.EqualTo("empty_click"));
    }

    [Test]
    public void サマリー行が末尾に出力される()
    {
        var input = string.Join("\n",
            "{\"ts\":\"2026-01-01T00:00:05Z\",\"type\":\"Click\",\"button\":\"Left\",\"rx\":100,\"ry\":100}",
            "{\"ts\":\"2026-01-01T00:00:06Z\",\"type\":\"TextInput\",\"text\":\"hello\"}"
        );

        var (actions, summary) = RunCorrelator(input, includeNoise: true);

        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.Type, Is.EqualTo("summary"));
        Assert.That(summary.SummaryType, Is.EqualTo("correlation"));
        Assert.That(summary.Metrics.TotalActions, Is.EqualTo(2));
    }

    [Test]
    public void systemタイプはnote付きでそのまま出力される()
    {
        var input = "{\"ts\":\"2026-01-01T00:00:05Z\",\"type\":\"system\",\"message\":\"hook restart\"}";

        var (actions, _) = RunCorrelator(input, includeNoise: true);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].Type, Is.EqualTo("system"));
        Assert.That(actions[0].Note, Is.EqualTo("hook restart"));
    }

    [Test]
    public void アプリログが時間窓内で紐付けられる()
    {
        var appLogs = new List<AppLogEntry>
        {
            new() { Ts = "2026-01-01T00:00:05.500Z", Type = "click", Control = "btnSearch" },
            new() { Ts = "2026-01-01T00:00:10Z", Type = "property_change", Control = "txtResult" }
        };

        var input = "{\"ts\":\"2026-01-01T00:00:05Z\",\"type\":\"Click\",\"button\":\"Left\",\"rx\":100,\"ry\":100}";

        var (actions, _) = RunCorrelator(input, appLogEntries: appLogs, includeNoise: true);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].AppLog, Is.Not.Null);
        Assert.That(actions[0].AppLog, Has.Count.EqualTo(1));
        Assert.That(actions[0].AppLog![0].Control, Is.EqualTo("btnSearch"));
    }

    [Test]
    public void explainモードで判定根拠が付与される()
    {
        var input = "{\"ts\":\"2026-01-01T00:00:05Z\",\"type\":\"Click\",\"button\":\"Left\",\"rx\":100,\"ry\":100}";

        var (actions, _) = RunCorrelator(input, includeNoise: true, explain: true);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].Explain, Is.Not.Null);
        Assert.That(actions[0].Explain!.NoiseReason, Does.Contain("empty_click"));
    }

    [Test]
    public void 座標なしアクションはターゲットが付与されない()
    {
        var input = "{\"ts\":\"2026-01-01T00:00:05Z\",\"type\":\"TextInput\",\"text\":\"hello\"}";

        var (actions, _) = RunCorrelator(input, includeNoise: true);

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(actions[0].Target, Is.Null);
    }
}
