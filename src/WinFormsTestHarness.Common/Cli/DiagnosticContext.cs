namespace WinFormsTestHarness.Common.Cli;

/// <summary>
/// --debug / --quiet フラグの状態を保持し、
/// 診断出力の制御を行うコンテキスト。
/// </summary>
public class DiagnosticContext
{
    /// <summary>--debug: 診断情報を stderr に出力</summary>
    public bool Debug { get; }

    /// <summary>--quiet: stderr 出力を抑制（エラーのみ）</summary>
    public bool Quiet { get; }

    public DiagnosticContext(bool debug, bool quiet)
    {
        Debug = debug;
        Quiet = quiet;
    }

    /// <summary>デバッグ情報を stderr に出力（--debug 時のみ）</summary>
    public void DebugLog(string message)
    {
        if (Debug)
            Console.Error.WriteLine($"[DEBUG] {message}");
    }

    /// <summary>警告を stderr に出力（--quiet 時は抑制）</summary>
    public void Warn(string message)
    {
        if (!Quiet)
            Console.Error.WriteLine($"Warning: {message}");
    }

    /// <summary>情報メッセージを stderr に出力（--quiet 時は抑制）</summary>
    public void Info(string message)
    {
        if (!Quiet)
            Console.Error.WriteLine(message);
    }

    /// <summary>エラーは常に stderr に出力（--quiet でも抑制しない）</summary>
    public static void Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
    }
}
