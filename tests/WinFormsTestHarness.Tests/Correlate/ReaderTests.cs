using NUnit.Framework;
using WinFormsTestHarness.Correlate.Readers;

namespace WinFormsTestHarness.Tests.Correlate;

[TestFixture]
public class ReaderTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wfth-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void UiaSnapshotReader_NDJSONを読み込みタイムスタンプ順にソートする()
    {
        var path = Path.Combine(_tempDir, "uia.ndjson");
        File.WriteAllText(path, string.Join("\n",
            "{\"ts\":\"2026-01-01T00:00:02Z\",\"automationId\":\"Form\",\"controlType\":\"Window\"}",
            "{\"ts\":\"2026-01-01T00:00:01Z\",\"automationId\":\"Form\",\"controlType\":\"Window\"}"
        ));

        var snapshots = UiaSnapshotReader.Read(path);

        Assert.That(snapshots, Has.Count.EqualTo(2));
        Assert.That(snapshots[0].Ts, Is.EqualTo("2026-01-01T00:00:01Z"));
        Assert.That(snapshots[1].Ts, Is.EqualTo("2026-01-01T00:00:02Z"));
    }

    [Test]
    public void AppLogReader_NDJSONを読み込みタイムスタンプ順にソートする()
    {
        var path = Path.Combine(_tempDir, "applog.ndjson");
        File.WriteAllText(path, string.Join("\n",
            "{\"ts\":\"2026-01-01T00:00:02Z\",\"type\":\"property_change\",\"control\":\"txtName\"}",
            "{\"ts\":\"2026-01-01T00:00:01Z\",\"type\":\"click\",\"control\":\"btnSearch\"}"
        ));

        var entries = AppLogReader.Read(path);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0].Ts, Is.EqualTo("2026-01-01T00:00:01Z"));
        Assert.That(entries[1].Ts, Is.EqualTo("2026-01-01T00:00:02Z"));
    }

    [Test]
    public void UiaSnapshotReader_不正行はスキップされる()
    {
        var path = Path.Combine(_tempDir, "uia.ndjson");
        File.WriteAllText(path, string.Join("\n",
            "{\"ts\":\"2026-01-01T00:00:01Z\",\"automationId\":\"Form\",\"controlType\":\"Window\"}",
            "invalid json line",
            "{\"ts\":\"2026-01-01T00:00:02Z\",\"automationId\":\"Form\",\"controlType\":\"Window\"}"
        ));

        var snapshots = UiaSnapshotReader.Read(path);

        Assert.That(snapshots, Has.Count.EqualTo(2));
    }

    [Test]
    public void UiaSnapshotReader_子ノード付きスナップショットを正しく読み込む()
    {
        var path = Path.Combine(_tempDir, "uia.ndjson");
        File.WriteAllText(path,
            "{\"ts\":\"2026-01-01T00:00:00Z\",\"automationId\":\"MainForm\",\"controlType\":\"Window\",\"children\":[{\"automationId\":\"btn1\",\"name\":\"ボタン\",\"controlType\":\"Button\"}]}"
        );

        var snapshots = UiaSnapshotReader.Read(path);

        Assert.That(snapshots, Has.Count.EqualTo(1));
        Assert.That(snapshots[0].Children, Has.Count.EqualTo(1));
        Assert.That(snapshots[0].Children![0].AutomationId, Is.EqualTo("btn1"));
    }
}
