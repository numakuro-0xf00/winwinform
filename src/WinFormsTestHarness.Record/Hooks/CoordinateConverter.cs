namespace WinFormsTestHarness.Record.Hooks;

/// <summary>
/// 座標変換ユーティリティ。スクリーン座標→ウィンドウ相対座標の変換。
/// Win32 API 不要の純粋な数学演算のみ。
/// </summary>
public static class CoordinateConverter
{
    /// <summary>
    /// スクリーン座標をウィンドウ相対座標に変換する。
    /// </summary>
    public static (int X, int Y) ToWindowRelative(int screenX, int screenY, int windowLeft, int windowTop)
    {
        return (screenX - windowLeft, screenY - windowTop);
    }

    /// <summary>
    /// スクリーン座標がウィンドウ矩形内にあるか判定する。
    /// </summary>
    public static bool IsInWindow(int screenX, int screenY, int windowLeft, int windowTop, int windowWidth, int windowHeight)
    {
        return screenX >= windowLeft && screenX < windowLeft + windowWidth
            && screenY >= windowTop && screenY < windowTop + windowHeight;
    }
}
