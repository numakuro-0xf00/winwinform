namespace WinFormsTestHarness.Record.Monitoring;

/// <summary>
/// システム時刻の抽象化。テスト時に FakeSystemClock で差し替え可能。
/// </summary>
public interface ISystemClock
{
    DateTime UtcNow { get; }
}
