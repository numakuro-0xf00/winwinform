using WinFormsTestHarness.Logger.Models;

namespace WinFormsTestHarness.Logger.Sinks;

/// <summary>
/// ログ出力先の抽象化インターフェース。
/// </summary>
internal interface ILogSink : IDisposable
{
    void Write(LogEntry entry);
    bool IsConnected { get; }
}
