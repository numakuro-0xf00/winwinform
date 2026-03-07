namespace WinFormsTestHarness.Record.Events;

/// <summary>
/// モニター構成情報。セッション開始イベントに含まれる。
/// </summary>
public class MonitorConfig
{
    /// <summary>モニター名</summary>
    public string Name { get; set; } = "";

    /// <summary>プライマリモニターか</summary>
    public bool IsPrimary { get; set; }

    /// <summary>作業領域の矩形</summary>
    public WindowRect Bounds { get; set; } = new(0, 0, 0, 0);

    /// <summary>DPI スケーリング（100 = 100%）</summary>
    public int DpiScale { get; set; } = 100;
}
