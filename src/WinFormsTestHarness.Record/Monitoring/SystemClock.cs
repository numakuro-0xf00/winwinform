namespace WinFormsTestHarness.Record.Monitoring;

/// <summary>
/// 実環境用のシステム時計。DateTime.UtcNow をそのまま返す。
/// </summary>
public class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
