using System.Drawing;
using WinFormsTestHarness.Capture;
using WinFormsTestHarness.Common.Timing;

namespace WinFormsTestHarness.Tests.Capture.Fakes;

/// <summary>
/// テスト用 IScreenCapturer モック。プログラム生成 Bitmap を返す。
/// </summary>
internal class FakeScreenCapturer : IScreenCapturer
{
    private readonly PreciseTimestamp _timestamp = new();
    private readonly List<Bitmap> _bitmaps = new();
    private int _captureIndex;

    public int CaptureCallCount { get; private set; }

    /// <summary>
    /// Capture() で返す Bitmap を追加する。
    /// 追加しない場合はデフォルトの白い Bitmap を返す。
    /// </summary>
    public void AddBitmap(Bitmap bitmap)
    {
        _bitmaps.Add(bitmap);
    }

    public CaptureResult Capture(string? triggeredBy)
    {
        CaptureCallCount++;

        Bitmap bitmap;
        if (_bitmaps.Count > 0)
        {
            bitmap = _bitmaps[_captureIndex % _bitmaps.Count];
            _captureIndex++;
        }
        else
        {
            bitmap = CreateDefaultBitmap();
        }

        return new CaptureResult
        {
            Timestamp = _timestamp.Now(),
            Width = bitmap.Width,
            Height = bitmap.Height,
            Bitmap = (Bitmap)bitmap.Clone(),
        };
    }

    public Rectangle GetWindowRect()
    {
        return new Rectangle(0, 0, 100, 100);
    }

    public static Bitmap CreateWhiteBitmap(int width = 100, int height = 100)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.FillRectangle(Brushes.White, 0, 0, width, height);
        return bmp;
    }

    public static Bitmap CreateColorBitmap(Color color, int width = 100, int height = 100)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 0, 0, width, height);
        return bmp;
    }

    private static Bitmap CreateDefaultBitmap()
    {
        return CreateWhiteBitmap();
    }
}
