namespace WinFormsTestHarness.Capture;

/// <summary>
/// RecordingSession に渡すキャプチャ統合設定。
/// </summary>
public class CaptureSettings
{
    public CaptureLevel Level { get; set; } = CaptureLevel.AfterOnly;
    public CaptureOptions Options { get; set; } = new();
    public string OutputDir { get; set; } = "./screenshots";
    public int AfterDelayMs { get; set; } = 300;
    public double DiffThreshold { get; set; } = 0.02;
}
