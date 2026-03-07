using System.Drawing;

namespace WinFormsTestHarness.Capture;

/// <summary>
/// スクリーンキャプチャのインターフェース。テスト用モック差し替え可能。
/// </summary>
public interface IScreenCapturer
{
    CaptureResult Capture(string? triggeredBy);
    Rectangle GetWindowRect();
}
