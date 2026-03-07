using System.Drawing;
using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Capture;

/// <summary>
/// キャプチャ結果。Bitmap と保存メタデータを保持する。
/// </summary>
public class CaptureResult : IDisposable
{
    public string Timestamp { get; set; } = "";
    public string? FilePath { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }
    public double DiffRatio { get; set; }
    public bool Skipped { get; set; }
    public string? ReuseFrom { get; set; }

    [JsonIgnore]
    public Bitmap? Bitmap { get; set; }

    public void Dispose()
    {
        Bitmap?.Dispose();
        Bitmap = null;
    }
}
