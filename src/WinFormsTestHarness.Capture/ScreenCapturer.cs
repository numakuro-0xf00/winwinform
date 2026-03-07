using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using WinFormsTestHarness.Common.Timing;

namespace WinFormsTestHarness.Capture;

/// <summary>
/// PrintWindow → CopyFromScreen フォールバックでウィンドウをキャプチャする。
/// Quality に応じたリサイズを行う。
/// </summary>
public class ScreenCapturer : IScreenCapturer, IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly CaptureOptions _options;
    private readonly PreciseTimestamp _timestamp;

    public ScreenCapturer(IntPtr hwnd, CaptureOptions options)
    {
        _hwnd = hwnd;
        _options = options;
        _timestamp = new PreciseTimestamp();
    }

    public CaptureResult Capture(string? triggeredBy)
    {
        var rect = GetWindowRect();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return new CaptureResult
            {
                Timestamp = _timestamp.Now(),
                Skipped = true,
            };
        }

        var bitmap = CaptureWindow(rect);
        var resized = ApplyQualityResize(bitmap);
        if (resized != bitmap)
            bitmap.Dispose();

        return new CaptureResult
        {
            Timestamp = _timestamp.Now(),
            Width = resized.Width,
            Height = resized.Height,
            Bitmap = resized,
        };
    }

    public Rectangle GetWindowRect()
    {
        if (NativeMethods.GetWindowRect(_hwnd, out var rect))
        {
            return new Rectangle(
                rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        return Rectangle.Empty;
    }

    private Bitmap CaptureWindow(Rectangle rect)
    {
        // PrintWindow でキャプチャを試みる
        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            var hdc = g.GetHdc();
            try
            {
                if (NativeMethods.PrintWindow(_hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT))
                {
                    return bitmap;
                }
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        // PrintWindow 失敗時は CopyFromScreen フォールバック
        bitmap.Dispose();
        bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(rect.X, rect.Y, 0, 0, rect.Size);
        }
        return bitmap;
    }

    private Bitmap ApplyQualityResize(Bitmap source)
    {
        var scale = _options.Quality switch
        {
            CaptureQuality.Low => 0.5,
            CaptureQuality.Medium => 0.75,
            _ => 1.0,
        };

        var newWidth = (int)(source.Width * scale);
        var newHeight = (int)(source.Height * scale);

        if (_options.MaxWidth.HasValue && newWidth > _options.MaxWidth.Value)
        {
            var ratio = (double)_options.MaxWidth.Value / newWidth;
            newWidth = _options.MaxWidth.Value;
            newHeight = (int)(newHeight * ratio);
        }

        if (newWidth == source.Width && newHeight == source.Height)
            return source;

        var resized = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(source, 0, 0, newWidth, newHeight);
        }
        return resized;
    }

    public void Dispose()
    {
        // No unmanaged resources to dispose
    }
}
