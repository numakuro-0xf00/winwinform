using NUnit.Framework;
using WinFormsTestHarness.Record.Hooks;

namespace WinFormsTestHarness.Tests.Record.Hooks;

[TestFixture]
public class CoordinateConverterTests
{
    [Test]
    public void ToWindowRelative_スクリーン座標をウィンドウ相対に変換()
    {
        var (x, y) = CoordinateConverter.ToWindowRelative(150, 250, 100, 200);
        Assert.That(x, Is.EqualTo(50));
        Assert.That(y, Is.EqualTo(50));
    }

    [Test]
    public void ToWindowRelative_ウィンドウ左上が原点の場合()
    {
        var (x, y) = CoordinateConverter.ToWindowRelative(0, 0, 0, 0);
        Assert.That(x, Is.EqualTo(0));
        Assert.That(y, Is.EqualTo(0));
    }

    [Test]
    public void ToWindowRelative_負の相対座標が返る場合()
    {
        var (x, y) = CoordinateConverter.ToWindowRelative(50, 50, 100, 100);
        Assert.That(x, Is.EqualTo(-50));
        Assert.That(y, Is.EqualTo(-50));
    }

    [Test]
    public void IsInWindow_ウィンドウ内の座標はtrue()
    {
        Assert.That(CoordinateConverter.IsInWindow(150, 250, 100, 200, 200, 200), Is.True);
    }

    [Test]
    public void IsInWindow_ウィンドウ外の座標はfalse()
    {
        Assert.That(CoordinateConverter.IsInWindow(50, 50, 100, 100, 200, 200), Is.False);
    }

    [Test]
    public void IsInWindow_境界上の座標は左上含む右下含まない()
    {
        // 左上の角はウィンドウ内
        Assert.That(CoordinateConverter.IsInWindow(100, 100, 100, 100, 200, 200), Is.True);
        // 右下の角はウィンドウ外
        Assert.That(CoordinateConverter.IsInWindow(300, 300, 100, 100, 200, 200), Is.False);
    }

    [Test]
    public void IsInWindow_右端と下端の境界値()
    {
        // windowLeft=100, windowTop=100, width=200, height=200
        // => 有効範囲は x:[100,299], y:[100,299]

        // 右端ぎりぎり内側 (299, 150) => true
        Assert.That(CoordinateConverter.IsInWindow(299, 150, 100, 100, 200, 200), Is.True);
        // 右端ぎりぎり外側 (300, 150) => false
        Assert.That(CoordinateConverter.IsInWindow(300, 150, 100, 100, 200, 200), Is.False);
        // 下端ぎりぎり内側 (150, 299) => true
        Assert.That(CoordinateConverter.IsInWindow(150, 299, 100, 100, 200, 200), Is.True);
        // 下端ぎりぎり外側 (150, 300) => false
        Assert.That(CoordinateConverter.IsInWindow(150, 300, 100, 100, 200, 200), Is.False);
    }

    [Test]
    public void ToWindowRelative_マルチモニター構成の大きなスクリーン座標でも正しく変換()
    {
        // 4K x 3枚構成: 3枚目のモニター上の座標
        var (x, y) = CoordinateConverter.ToWindowRelative(8500, 1200, 7680, 0);
        Assert.That(x, Is.EqualTo(820));
        Assert.That(y, Is.EqualTo(1200));
    }
}
