namespace WinFormsTestHarness.Capture;

/// <summary>
/// キャプチャ設定。画質・リサイズ・JPEG品質を制御する。
/// </summary>
public class CaptureOptions
{
    public CaptureQuality Quality { get; set; } = CaptureQuality.Medium;
    public int? MaxWidth { get; set; }
    public int JpegQuality { get; set; } = 85;
}
