namespace WinFormsTestHarness.Record.Hooks;

/// <summary>
/// 仮想キーコード→キー名の解決。
/// Win32 API 不要の静的辞書ベース。
/// </summary>
public static class KeyNameResolver
{
    private static readonly Dictionary<int, string> VkNames = new()
    {
        // Modifier keys
        { 0x10, "Shift" }, { 0x11, "Ctrl" }, { 0x12, "Alt" },
        { 0xA0, "LShift" }, { 0xA1, "RShift" },
        { 0xA2, "LCtrl" }, { 0xA3, "RCtrl" },
        { 0xA4, "LAlt" }, { 0xA5, "RAlt" },
        { 0x5B, "LWin" }, { 0x5C, "RWin" },

        // Function keys
        { 0x70, "F1" }, { 0x71, "F2" }, { 0x72, "F3" }, { 0x73, "F4" },
        { 0x74, "F5" }, { 0x75, "F6" }, { 0x76, "F7" }, { 0x77, "F8" },
        { 0x78, "F9" }, { 0x79, "F10" }, { 0x7A, "F11" }, { 0x7B, "F12" },

        // Navigation
        { 0x21, "PageUp" }, { 0x22, "PageDown" },
        { 0x23, "End" }, { 0x24, "Home" },
        { 0x25, "Left" }, { 0x26, "Up" }, { 0x27, "Right" }, { 0x28, "Down" },

        // Editing
        { 0x08, "Backspace" }, { 0x09, "Tab" }, { 0x0D, "Enter" },
        { 0x1B, "Escape" }, { 0x20, "Space" },
        { 0x2D, "Insert" }, { 0x2E, "Delete" },

        // Lock keys
        { 0x14, "CapsLock" }, { 0x90, "NumLock" }, { 0x91, "ScrollLock" },

        // Misc
        { 0x2C, "PrintScreen" }, { 0x13, "Pause" },
        { 0x5D, "ContextMenu" },
    };

    private static readonly HashSet<int> ModifierVks = new()
    {
        0x10, 0x11, 0x12, // Shift, Ctrl, Alt
        0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, // L/R Shift, Ctrl, Alt
        0x5B, 0x5C, // L/R Win
    };

    private static readonly HashSet<int> SpecialVks = new()
    {
        0x08, 0x09, 0x0D, 0x1B, 0x20, // Backspace, Tab, Enter, Escape, Space
        0x2D, 0x2E, // Insert, Delete
        0x21, 0x22, 0x23, 0x24, // PageUp, PageDown, End, Home
        0x25, 0x26, 0x27, 0x28, // Arrow keys
        0x70, 0x71, 0x72, 0x73, 0x74, 0x75, // F1-F6
        0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B, // F7-F12
        0x2C, 0x13, // PrintScreen, Pause
    };

    /// <summary>
    /// 仮想キーコードからキー名を解決する。
    /// 辞書にない場合は "VK_0xHH" 形式で返す。
    /// 0-9, A-Z はそのまま文字として返す。
    /// </summary>
    public static string Resolve(int vkCode)
    {
        if (VkNames.TryGetValue(vkCode, out var name))
            return name;

        // 0-9
        if (vkCode >= 0x30 && vkCode <= 0x39)
            return ((char)vkCode).ToString();

        // A-Z
        if (vkCode >= 0x41 && vkCode <= 0x5A)
            return ((char)vkCode).ToString();

        // Numpad 0-9
        if (vkCode >= 0x60 && vkCode <= 0x69)
            return $"Numpad{vkCode - 0x60}";

        return $"VK_0x{vkCode:X2}";
    }

    /// <summary>修飾キーか判定する</summary>
    public static bool IsModifier(int vkCode) => ModifierVks.Contains(vkCode);

    /// <summary>特殊キー（非文字キー）か判定する</summary>
    public static bool IsSpecialKey(int vkCode) => SpecialVks.Contains(vkCode) || ModifierVks.Contains(vkCode);
}
