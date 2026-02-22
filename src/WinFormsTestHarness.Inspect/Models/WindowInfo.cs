namespace WinFormsTestHarness.Inspect.Models;

public record WindowInfo(
    string Hwnd,      // "0x001A0F32" format
    string Title,
    string Process,
    int Pid
);
