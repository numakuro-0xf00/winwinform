using System.Drawing;
using NUnit.Framework;
using WinFormsTestHarness.Capture;
using WinFormsTestHarness.Tests.Capture.Fakes;

namespace WinFormsTestHarness.Tests.Capture;

[TestFixture]
public class CaptureFileWriterTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"capture_test_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void 連番命名でファイルが保存される()
    {
        var writer = new CaptureFileWriter(_tempDir);
        using var bitmap = FakeScreenCapturer.CreateWhiteBitmap();
        var result = new CaptureResult { Bitmap = bitmap, Width = 100, Height = 100 };

        writer.Save(result, "test");

        Assert.That(result.FilePath, Does.EndWith("0001_test.png"));
        Assert.That(File.Exists(result.FilePath), Is.True);
    }

    [Test]
    public void suffix名がファイル名に反映される()
    {
        var writer = new CaptureFileWriter(_tempDir);
        using var bitmap = FakeScreenCapturer.CreateWhiteBitmap();
        var result = new CaptureResult { Bitmap = bitmap, Width = 100, Height = 100 };

        writer.Save(result, "before");

        Assert.That(Path.GetFileName(result.FilePath), Is.EqualTo("0001_before.png"));
    }

    [Test]
    public void ディレクトリが自動作成される()
    {
        var subDir = Path.Combine(_tempDir, "nested", "dir");
        var writer = new CaptureFileWriter(subDir);
        using var bitmap = FakeScreenCapturer.CreateWhiteBitmap();
        var result = new CaptureResult { Bitmap = bitmap, Width = 100, Height = 100 };

        writer.Save(result, "auto");

        Assert.That(Directory.Exists(subDir), Is.True);
        Assert.That(File.Exists(result.FilePath), Is.True);
    }

    [Test]
    public void FileSizeが設定される()
    {
        var writer = new CaptureFileWriter(_tempDir);
        using var bitmap = FakeScreenCapturer.CreateWhiteBitmap();
        var result = new CaptureResult { Bitmap = bitmap, Width = 100, Height = 100 };

        writer.Save(result, "size");

        Assert.That(result.FileSize, Is.GreaterThan(0));
    }

    [Test]
    public void 連番がインクリメントされる()
    {
        var writer = new CaptureFileWriter(_tempDir);

        using var bmp1 = FakeScreenCapturer.CreateWhiteBitmap();
        var result1 = new CaptureResult { Bitmap = bmp1, Width = 100, Height = 100 };
        writer.Save(result1, "seq");
        Assert.That(Path.GetFileName(result1.FilePath), Is.EqualTo("0001_seq.png"));

        using var bmp2 = FakeScreenCapturer.CreateWhiteBitmap();
        var result2 = new CaptureResult { Bitmap = bmp2, Width = 100, Height = 100 };
        writer.Save(result2, "seq");
        Assert.That(Path.GetFileName(result2.FilePath), Is.EqualTo("0002_seq.png"));

        using var bmp3 = FakeScreenCapturer.CreateWhiteBitmap();
        var result3 = new CaptureResult { Bitmap = bmp3, Width = 100, Height = 100 };
        writer.Save(result3, "seq");
        Assert.That(Path.GetFileName(result3.FilePath), Is.EqualTo("0003_seq.png"));
    }

    [Test]
    public void Bitmapがnullの場合は何もしない()
    {
        var writer = new CaptureFileWriter(_tempDir);
        var result = new CaptureResult { Bitmap = null, Width = 0, Height = 0 };

        writer.Save(result, "null_test");

        Assert.That(result.FilePath, Is.Null);
        Assert.That(Directory.Exists(_tempDir), Is.False, "ディレクトリも作成されないべき");
    }
}
