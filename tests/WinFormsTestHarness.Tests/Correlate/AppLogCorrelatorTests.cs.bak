using System.Text.Json;
using NUnit.Framework;
using WinFormsTestHarness.Correlate.Correlation;
using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Tests.Correlate;

[TestFixture]
public class AppLogCorrelatorTests
{
    private const int WindowMs = 2000;

    private static string Ts(int ms) => CorrelateTestHelper.Ts(ms);

    private static string ClickLine(int ms, int rx = 50, int ry = 100)
        => $"{{\"ts\":\"{Ts(ms)}\",\"type\":\"Click\",\"button\":\"Left\",\"sx\":100,\"sy\":200,\"rx\":{rx},\"ry\":{ry}}}";

    private List<JsonElement> RunCorrelator(string[] inputLines, List<UiaChangeEvent>? uiaEvents = null, List<AppLogEntry>? appLogs = null)
    {
        var correlator = new TimeWindowCorrelator(
            uiaEvents: uiaEvents,
            appLogs: appLogs,
            windowMs: WindowMs,
            includeNoise: true);

        var input = new StringReader(string.Join("\n", inputLines));
        var output = new StringWriter();
        correlator.Process(input, output);

        return output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                using var doc = JsonDocument.Parse(line);
                return doc.RootElement.Clone();
            })
            .ToList();
    }

    [Test]
    public void AutomationId一致のアプリログが優先される()
    {
        var uia = new List<UiaChangeEvent>
        {
            MakeUiaSnapshot(100, "btnSearch"),
        };
        var appLogs = new List<AppLogEntry>
        {
            new() { Ts = Ts(150), Type = "event", Control = "btnSearch", Event = "Click" },
            new() { Ts = Ts(120), Type = "prop", Control = "txtName", Prop = "Text" },
        };

        var results = RunCorrelator(
            new[] { ClickLine(100) },
            uiaEvents: uia,
            appLogs: appLogs);

        var actions = results.Where(r => r.GetProperty("type").GetString() != "summary").ToList();
        var appLog = actions[0].GetProperty("appLog");
        Assert.That(appLog.GetArrayLength(), Is.EqualTo(2));
        // AutomationId 一致のログが先頭
        Assert.That(appLog[0].GetProperty("control").GetString(), Is.EqualTo("btnSearch"));
    }

    [Test]
    public void 時間窓内の複数ログが全て紐付く()
    {
        var uia = new List<UiaChangeEvent> { MakeUiaSnapshot(100, "btn1") };
        var appLogs = new List<AppLogEntry>
        {
            new() { Ts = Ts(100), Type = "event", Control = "btn1", Event = "Click" },
            new() { Ts = Ts(200), Type = "prop", Control = "txt1", Prop = "Text", Old = "", New = "test" },
            new() { Ts = Ts(500), Type = "form_open", Form = "SearchForm" },
        };

        var results = RunCorrelator(
            new[] { ClickLine(100) },
            uiaEvents: uia,
            appLogs: appLogs);

        var actions = results.Where(r => r.GetProperty("type").GetString() != "summary").ToList();
        Assert.That(actions[0].GetProperty("appLog").GetArrayLength(), Is.EqualTo(3));
    }

    [Test]
    public void 時間窓外のログは紐付かない()
    {
        var uia = new List<UiaChangeEvent> { MakeUiaSnapshot(100, "btn1") };
        var appLogs = new List<AppLogEntry>
        {
            new() { Ts = Ts(100), Type = "event", Control = "btn1", Event = "Click" },
            new() { Ts = Ts(3000), Type = "event", Control = "btn2", Event = "Click" }, // 窓外
        };

        var results = RunCorrelator(
            new[] { ClickLine(100) },
            uiaEvents: uia,
            appLogs: appLogs);

        var actions = results.Where(r => r.GetProperty("type").GetString() != "summary").ToList();
        Assert.That(actions[0].GetProperty("appLog").GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public void サマリーのwithAppLogカウントが正しい()
    {
        var uia = new List<UiaChangeEvent> { MakeUiaSnapshot(100, "btn1") };
        var appLogs = new List<AppLogEntry>
        {
            new() { Ts = Ts(150), Type = "event", Control = "btn1" },
        };

        var results = RunCorrelator(
            new[] { ClickLine(100) },
            uiaEvents: uia,
            appLogs: appLogs);

        var summaries = results.Where(r => r.GetProperty("type").GetString() == "summary").ToList();
        var corr = summaries.First(s => s.GetProperty("summaryType").GetString() == "correlation");
        Assert.Multiple(() =>
        {
            Assert.That(corr.GetProperty("metrics").GetProperty("withAppLog").GetInt32(), Is.EqualTo(1));
            Assert.That(corr.GetProperty("metrics").GetProperty("withoutAppLog").GetInt32(), Is.EqualTo(0));
        });
    }

    [Test]
    public void アプリログなしの場合もサマリーカウントが正しい()
    {
        var uia = new List<UiaChangeEvent> { MakeUiaSnapshot(100, "btn1") };

        var results = RunCorrelator(
            new[] { ClickLine(100) },
            uiaEvents: uia);

        var summaries = results.Where(r => r.GetProperty("type").GetString() == "summary").ToList();
        var corr = summaries.First(s => s.GetProperty("summaryType").GetString() == "correlation");
        Assert.Multiple(() =>
        {
            Assert.That(corr.GetProperty("metrics").GetProperty("withAppLog").GetInt32(), Is.EqualTo(0));
            Assert.That(corr.GetProperty("metrics").GetProperty("withoutAppLog").GetInt32(), Is.EqualTo(1));
        });
    }

    private static UiaChangeEvent MakeUiaSnapshot(int ms, string automationId) => new()
    {
        Ts = Ts(ms),
        AutomationId = automationId,
        Name = "Button",
        ControlType = "Button",
        Rect = new UiaRect(30, 80, 100, 30),
    };
}
