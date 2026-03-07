using System.Text.Json;
using NUnit.Framework;
using WinFormsTestHarness.Aggregate.Aggregation;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.IO;

namespace WinFormsTestHarness.Tests.Aggregate;

[TestFixture]
public class ActionBuilderTests
{
    private List<JsonElement> ParseOutput(string output)
    {
        var text = output.TrimEnd();
        if (string.IsNullOrEmpty(text))
            return new List<JsonElement>();

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
    }

    private (int exitCode, List<JsonElement> output) RunBuilder(string input,
        int clickTimeoutMs = 300, int dblclickTimeoutMs = 500, int textTimeoutMs = 500)
    {
        using var reader = new StringReader(input);
        using var outputBuffer = new StringWriter();
        using var writer = new NdJsonWriter(outputBuffer);
        var diag = new DiagnosticContext(false, true);

        var builder = new ActionBuilder(reader, writer, diag,
            clickTimeoutMs, dblclickTimeoutMs, textTimeoutMs);

        var exitCode = builder.Run();
        return (exitCode, ParseOutput(outputBuffer.ToString()));
    }

    [Test]
    public void Sessionイベントはパススルーされる()
    {
        var input = "{\"ts\":\"2026-01-01T00:00:00.000Z\",\"type\":\"session\",\"action\":\"start\",\"process\":\"App\",\"pid\":123}";

        var (exitCode, output) = RunBuilder(input);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(output, Has.Count.EqualTo(1));
        Assert.That(output[0].GetProperty("type").GetString(), Is.EqualTo("session"));
        Assert.That(output[0].GetProperty("action").GetString(), Is.EqualTo("start"));
    }

    [Test]
    public void 不正行はスキップされる()
    {
        var input = "not valid json\n{\"ts\":\"2026-01-01T00:00:00.000Z\",\"type\":\"session\",\"action\":\"start\"}";

        var (exitCode, output) = RunBuilder(input);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(output, Has.Count.EqualTo(1));
        Assert.That(output[0].GetProperty("type").GetString(), Is.EqualTo("session"));
    }

    [Test]
    public void DemoRecordNdjson_パイプライン統合テスト()
    {
        var demoPath = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "demo", "record.ndjson");

        // テスト環境によるパス解決のフォールバック
        if (!File.Exists(demoPath))
        {
            demoPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "..", "demo", "record.ndjson"));
        }

        Assert.That(File.Exists(demoPath), Is.True, $"demo/record.ndjson not found at {demoPath}");

        var input = File.ReadAllText(demoPath);
        var (exitCode, output) = RunBuilder(input);

        Assert.That(exitCode, Is.EqualTo(0));

        // 期待される出力タイプを順に確認
        var types = output.Select(e => e.GetProperty("type").GetString()).ToList();

        Assert.That(types[0], Is.EqualTo("session"), "session start passthrough");
        Assert.That(types[1], Is.EqualTo("Click"), "LeftDown+LeftUp(50ms) → Click");
        Assert.That(types[2], Is.EqualTo("TextInput"), "T+a+n → TextInput");
        Assert.That(types[3], Is.EqualTo("SpecialKey"), "Enter → SpecialKey");
        Assert.That(types[4], Is.EqualTo("DoubleClick"), "2x Click(80ms) → DoubleClick");
        Assert.That(types[5], Is.EqualTo("session"), "session stop passthrough");

        // TextInput の内容確認
        var textInput = output.First(e => e.GetProperty("type").GetString() == "TextInput");
        Assert.That(textInput.GetProperty("text").GetString(), Is.EqualTo("Tan"));

        // SpecialKey の内容確認
        var specialKey = output.First(e => e.GetProperty("type").GetString() == "SpecialKey");
        Assert.That(specialKey.GetProperty("key").GetString(), Is.EqualTo("Enter"));
    }

    [Test]
    public void 空入力で正常終了()
    {
        var (exitCode, output) = RunBuilder("");

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(output, Has.Count.EqualTo(0));
    }

    [Test]
    public void キー入力中にマウスクリックでTextInputがフラッシュされる()
    {
        var input = string.Join("\n",
            "{\"ts\":\"2026-01-01T00:00:00.000Z\",\"type\":\"key\",\"action\":\"down\",\"vk\":72,\"key\":\"H\",\"scan\":0,\"char\":\"H\"}",
            "{\"ts\":\"2026-01-01T00:00:00.050Z\",\"type\":\"mouse\",\"action\":\"LeftDown\",\"sx\":100,\"sy\":200,\"rx\":50,\"ry\":100}",
            "{\"ts\":\"2026-01-01T00:00:00.100Z\",\"type\":\"mouse\",\"action\":\"LeftUp\",\"sx\":100,\"sy\":200,\"rx\":50,\"ry\":100}");

        var (exitCode, output) = RunBuilder(input);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(output, Has.Count.EqualTo(2));
        Assert.That(output[0].GetProperty("type").GetString(), Is.EqualTo("TextInput"));
        Assert.That(output[0].GetProperty("text").GetString(), Is.EqualTo("H"));
        Assert.That(output[1].GetProperty("type").GetString(), Is.EqualTo("Click"));
    }
}
