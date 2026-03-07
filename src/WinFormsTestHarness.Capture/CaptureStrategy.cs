namespace WinFormsTestHarness.Capture;

/// <summary>
/// CaptureLevel に応じて before/after キャプチャを制御する。
/// </summary>
public class CaptureStrategy
{
    private readonly IScreenCapturer _capturer;
    private readonly DiffDetector _diffDetector;
    private readonly CaptureFileWriter _fileWriter;
    private readonly CaptureLevel _level;
    private readonly int _afterDelayMs;

    public CaptureStrategy(
        IScreenCapturer capturer,
        DiffDetector diffDetector,
        CaptureFileWriter fileWriter,
        CaptureLevel level,
        int afterDelayMs = 300)
    {
        _capturer = capturer;
        _diffDetector = diffDetector;
        _fileWriter = fileWriter;
        _level = level;
        _afterDelayMs = afterDelayMs;
    }

    /// <summary>
    /// 入力イベント前のキャプチャ。Level が BeforeAfter 未満なら null を返す。
    /// </summary>
    public CaptureResult? CaptureBeforeInput(string triggeredBy)
    {
        if (_level < CaptureLevel.BeforeAfter)
            return null;

        return CaptureWithDiff(triggeredBy, "before");
    }

    /// <summary>
    /// 入力イベント後のキャプチャ。Level が AfterOnly 未満なら null を返す。
    /// Task.Delay で UI 反応を待つ。
    /// </summary>
    public async Task<CaptureResult?> CaptureAfterInputAsync(string triggeredBy, int? delayMs = null)
    {
        if (_level < CaptureLevel.AfterOnly)
            return null;

        var delay = delayMs ?? _afterDelayMs;
        if (delay > 0)
            await Task.Delay(delay);

        return CaptureWithDiff(triggeredBy, "after");
    }

    private CaptureResult? CaptureWithDiff(string triggeredBy, string suffix)
    {
        var result = _capturer.Capture(triggeredBy);
        if (result.Skipped || result.Bitmap == null)
            return result;

        var diffRatio = _diffDetector.CalculateDiffRatio(result.Bitmap);
        result.DiffRatio = diffRatio < 0 ? 0.0 : diffRatio;

        // Level=All は差分検知をバイパス
        if (_level != CaptureLevel.All && !_diffDetector.IsChanged(diffRatio))
        {
            result.Skipped = true;
            result.Dispose();
            return result;
        }

        _fileWriter.Save(result, suffix);
        return result;
    }
}
