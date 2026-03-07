using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Tests.Correlate;

/// <summary>
/// Correlate テスト共通ヘルパー。Ts() や UiaChangeEvent 生成を共有する。
/// </summary>
internal static class CorrelateTestHelper
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 2, 23, 10, 0, 0, TimeSpan.Zero);

    public static string Ts(int ms) =>
        BaseTime.AddMilliseconds(ms).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    public static UiaChangeEvent MakeUiaEvent(
        int ms, string automationId, string name,
        string controlType, int x, int y, int w, int h,
        List<UiaChangeEvent>? children = null) => new()
    {
        Ts = Ts(ms),
        AutomationId = automationId,
        Name = name,
        ControlType = controlType,
        Rect = new UiaRect(x, y, w, h),
        Children = children,
    };
}
