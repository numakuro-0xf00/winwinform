using NUnit.Framework;
using WinFormsTestHarness.Capture;
using WinFormsTestHarness.Tests.Capture.Fakes;

namespace WinFormsTestHarness.Tests.Capture;

[TestFixture]
public class CaptureStrategyTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"strategy_test_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public async Task Level_None_BeforeもAfterもnull()
    {
        var capturer = new FakeScreenCapturer();
        var strategy = CreateStrategy(capturer, CaptureLevel.None);

        var before = strategy.CaptureBeforeInput("test");
        var after = await strategy.CaptureAfterInputAsync("test", delayMs: 0);

        Assert.That(before, Is.Null);
        Assert.That(after, Is.Null);
        Assert.That(capturer.CaptureCallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Level_AfterOnly_Beforeはnull_Afterは有効()
    {
        var capturer = new FakeScreenCapturer();
        var strategy = CreateStrategy(capturer, CaptureLevel.AfterOnly);

        var before = strategy.CaptureBeforeInput("test");
        using var after = await strategy.CaptureAfterInputAsync("test", delayMs: 0);

        Assert.That(before, Is.Null);
        Assert.That(after, Is.Not.Null);
        Assert.That(capturer.CaptureCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Level_BeforeAfter_両方有効()
    {
        var capturer = new FakeScreenCapturer();
        var strategy = CreateStrategy(capturer, CaptureLevel.BeforeAfter);

        using var before = strategy.CaptureBeforeInput("test");
        using var after = await strategy.CaptureAfterInputAsync("test", delayMs: 0);

        Assert.That(before, Is.Not.Null);
        Assert.That(after, Is.Not.Null);
        Assert.That(capturer.CaptureCallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task 差分なしの場合はスキップされる()
    {
        var capturer = new FakeScreenCapturer();
        var strategy = CreateStrategy(capturer, CaptureLevel.AfterOnly);

        // 初回（常に changed）
        using var first = await strategy.CaptureAfterInputAsync("test", delayMs: 0);
        Assert.That(first, Is.Not.Null);
        Assert.That(first!.Skipped, Is.False);

        // 2回目（同一内容→スキップ）
        using var second = await strategy.CaptureAfterInputAsync("test", delayMs: 0);
        Assert.That(second, Is.Not.Null);
        Assert.That(second!.Skipped, Is.True);
    }

    [Test]
    public async Task Level_All_差分スキップをバイパス()
    {
        var capturer = new FakeScreenCapturer();
        var strategy = CreateStrategy(capturer, CaptureLevel.All);

        // 初回
        using var first = await strategy.CaptureAfterInputAsync("test", delayMs: 0);
        Assert.That(first!.Skipped, Is.False);

        // 2回目（Level=All なのでスキップしない）
        using var second = await strategy.CaptureAfterInputAsync("test", delayMs: 0);
        Assert.That(second, Is.Not.Null);
        Assert.That(second!.Skipped, Is.False);
    }

    [Test]
    public async Task 差分なしでもファイルパスが未設定()
    {
        var capturer = new FakeScreenCapturer();
        var strategy = CreateStrategy(capturer, CaptureLevel.AfterOnly);

        using var first = await strategy.CaptureAfterInputAsync("test", delayMs: 0);
        Assert.That(first!.FilePath, Is.Not.Null, "初回はファイル保存されるべき");

        using var second = await strategy.CaptureAfterInputAsync("test", delayMs: 0);
        Assert.That(second!.FilePath, Is.Null, "スキップ時はファイル未保存");
    }

    private CaptureStrategy CreateStrategy(FakeScreenCapturer capturer, CaptureLevel level)
    {
        var diffDetector = new DiffDetector(threshold: 0.02);
        var fileWriter = new CaptureFileWriter(_tempDir);
        return new CaptureStrategy(capturer, diffDetector, fileWriter, level, afterDelayMs: 0);
    }
}
