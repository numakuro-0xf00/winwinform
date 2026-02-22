namespace WinFormsTestHarness.Common.Models;

/// <summary>
/// トップレベルウィンドウの情報。
/// wfth-inspect, wfth-record, wfth-capture 等で共有。
/// </summary>
public record WindowInfo(
    string Hwnd,      // "0x001A0F32" format
    string Title,
    string Process,
    int Pid
);
