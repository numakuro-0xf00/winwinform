using NUnit.Framework;
using WinFormsTestHarness.Correlate.Readers;

namespace WinFormsTestHarness.Tests.Correlate;

[TestFixture]
public class ScreenshotIndexTests
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
    public void beforeとafterのスクリーンショットをインデックスする()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "0001_before.png"), new byte[] { 0x89 });
        File.WriteAllBytes(Path.Combine(_tempDir, "0001_after.png"), new byte[] { 0x89 });
        File.WriteAllBytes(Path.Combine(_tempDir, "0002_before.png"), new byte[] { 0x89 });

        var index = new ScreenshotIndex(_tempDir);

        Assert.That(index.GetBefore(1), Is.Not.Null);
        Assert.That(index.GetAfter(1), Is.Not.Null);
        Assert.That(index.GetBefore(2), Is.Not.Null);
        Assert.That(index.GetAfter(2), Is.Null);
        Assert.That(index.Count, Is.EqualTo(3));
    }

    [Test]
    public void 存在しないseqはnullを返す()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "0001_before.png"), new byte[] { 0x89 });

        var index = new ScreenshotIndex(_tempDir);

        Assert.That(index.GetBefore(99), Is.Null);
        Assert.That(index.GetAfter(99), Is.Null);
    }

    [Test]
    public void 不正なファイル名はスキップされる()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "invalid.png"), new byte[] { 0x89 });
        File.WriteAllBytes(Path.Combine(_tempDir, "abc_before.png"), new byte[] { 0x89 });
        File.WriteAllBytes(Path.Combine(_tempDir, "0001_before.png"), new byte[] { 0x89 });

        var index = new ScreenshotIndex(_tempDir);

        Assert.That(index.Count, Is.EqualTo(1));
    }

    [Test]
    public void 存在しないディレクトリでは空のインデックスが作成される()
    {
        var index = new ScreenshotIndex("/nonexistent/dir");

        Assert.That(index.Count, Is.EqualTo(0));
        Assert.That(index.GetBefore(1), Is.Null);
    }
}
