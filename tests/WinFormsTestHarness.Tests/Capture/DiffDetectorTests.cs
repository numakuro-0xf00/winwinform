using System.Drawing;
using NUnit.Framework;
using WinFormsTestHarness.Capture;
using WinFormsTestHarness.Tests.Capture.Fakes;

namespace WinFormsTestHarness.Tests.Capture;

[TestFixture]
public class DiffDetectorTests
{
    [Test]
    public void 初回は常にchanged扱い()
    {
        var detector = new DiffDetector();
        using var bitmap = FakeScreenCapturer.CreateWhiteBitmap();

        var ratio = detector.CalculateDiffRatio(bitmap);

        Assert.That(ratio, Is.LessThan(0), "初回は負の値（changed扱い）を返すべき");
        Assert.That(detector.IsChanged(ratio), Is.True);
    }

    [Test]
    public void 同一Bitmapでunchanged()
    {
        var detector = new DiffDetector(threshold: 0.02);
        using var bitmap1 = FakeScreenCapturer.CreateWhiteBitmap();
        using var bitmap2 = FakeScreenCapturer.CreateWhiteBitmap();

        detector.CalculateDiffRatio(bitmap1); // 初回
        var ratio = detector.CalculateDiffRatio(bitmap2);

        Assert.That(ratio, Is.LessThan(0.02), "同一内容の Bitmap は閾値未満であるべき");
        Assert.That(detector.IsChanged(ratio), Is.False);
    }

    [Test]
    public void 異なるBitmapでchanged()
    {
        var detector = new DiffDetector(threshold: 0.02);
        using var white = FakeScreenCapturer.CreateWhiteBitmap();
        using var black = FakeScreenCapturer.CreateColorBitmap(Color.Black);

        detector.CalculateDiffRatio(white); // 初回
        var ratio = detector.CalculateDiffRatio(black);

        Assert.That(ratio, Is.GreaterThanOrEqualTo(0.02), "大きく異なる Bitmap は閾値以上であるべき");
        Assert.That(detector.IsChanged(ratio), Is.True);
    }

    [Test]
    public void IsChanged_ちょうど閾値はchanged()
    {
        var detector = new DiffDetector(threshold: 0.5);
        Assert.That(detector.IsChanged(0.5), Is.True, "閾値と等しい場合は changed");
    }

    [Test]
    public void IsChanged_閾値未満はunchanged()
    {
        var detector = new DiffDetector(threshold: 0.5);
        Assert.That(detector.IsChanged(0.499), Is.False, "閾値未満は unchanged");
    }

    [Test]
    public void IsChanged_負の値は常にchanged()
    {
        var detector = new DiffDetector(threshold: 0.5);
        Assert.That(detector.IsChanged(-1.0), Is.True, "初回を示す負の値は常に changed");
    }

    [Test]
    public void 高い閾値で微小な差分はunchanged()
    {
        var detector = new DiffDetector(threshold: 0.99);
        using var white = FakeScreenCapturer.CreateWhiteBitmap();
        using var almostWhite = FakeScreenCapturer.CreateColorBitmap(Color.FromArgb(254, 254, 254));

        detector.CalculateDiffRatio(white);
        var ratio = detector.CalculateDiffRatio(almostWhite);

        Assert.That(detector.IsChanged(ratio), Is.False, "微小な差分は高い閾値では unchanged");
    }
}
