using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WinFormsTestHarness.Capture;

/// <summary>
/// 64x48 サムネイル + グレースケール輝度比較で画像差分を検知する。
/// </summary>
public class DiffDetector
{
    private const int ThumbWidth = 64;
    private const int ThumbHeight = 48;

    private readonly double _threshold;
    private readonly object _lock = new();
    private byte[]? _previousPixels;

    public DiffDetector(double threshold = 0.02)
    {
        _threshold = threshold;
    }

    public bool HasChanged(Bitmap bitmap)
    {
        return CalculateDiffRatio(bitmap) >= 0;
    }

    /// <summary>
    /// 差分比率を計算する。初回は常に -1.0 を返す（changed 扱い）。
    /// </summary>
    public double CalculateDiffRatio(Bitmap bitmap)
    {
        var currentPixels = ToGrayscalePixels(bitmap);

        lock (_lock)
        {
            if (_previousPixels == null)
            {
                _previousPixels = currentPixels;
                return -1.0; // 初回は常に changed
            }

            var diffCount = 0;
            var totalPixels = currentPixels.Length;

            for (var i = 0; i < totalPixels; i++)
            {
                if (Math.Abs(currentPixels[i] - _previousPixels[i]) > 10)
                    diffCount++;
            }

            var ratio = (double)diffCount / totalPixels;
            _previousPixels = currentPixels;
            return ratio;
        }
    }

    /// <summary>
    /// 差分比率が閾値を超えたか（初回は常に true）。
    /// </summary>
    public bool IsChanged(double diffRatio)
    {
        return diffRatio < 0 || diffRatio >= _threshold;
    }

    private static byte[] ToGrayscalePixels(Bitmap bitmap)
    {
        using var thumb = new Bitmap(ThumbWidth, ThumbHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(thumb))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(bitmap, 0, 0, ThumbWidth, ThumbHeight);
        }

        var pixels = new byte[ThumbWidth * ThumbHeight];
        var data = thumb.LockBits(
            new Rectangle(0, 0, ThumbWidth, ThumbHeight),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            unsafe
            {
                var ptr = (byte*)data.Scan0;
                for (var i = 0; i < pixels.Length; i++)
                {
                    var offset = i * 4;
                    var b = ptr[offset];
                    var g2 = ptr[offset + 1];
                    var r = ptr[offset + 2];
                    pixels[i] = (byte)(0.299 * r + 0.587 * g2 + 0.114 * b);
                }
            }
        }
        finally
        {
            thumb.UnlockBits(data);
        }

        return pixels;
    }
}
