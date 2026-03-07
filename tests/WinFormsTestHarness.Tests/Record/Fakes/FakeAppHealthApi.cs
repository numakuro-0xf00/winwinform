using WinFormsTestHarness.Record.Monitoring;

namespace WinFormsTestHarness.Tests.Record.Fakes;

/// <summary>
/// テスト用 IAppHealthApi Fake 実装。
/// </summary>
public class FakeAppHealthApi : IAppHealthApi
{
    public bool ProcessAlive { get; set; } = true;
    public bool WindowResponsive { get; set; } = true;

    public bool IsProcessAlive(uint pid) => ProcessAlive;
    public bool IsWindowResponsive(IntPtr hwnd) => WindowResponsive;
}
