using WinFormsTestHarness.Record.Monitoring;

namespace WinFormsTestHarness.Tests.Record.Fakes;

/// <summary>
/// テスト用 IProbeInput Fake 実装。
/// </summary>
public class FakeProbeInput : IProbeInput
{
    public int ProbeCount { get; private set; }

    public void SendProbe()
    {
        ProbeCount++;
    }
}
