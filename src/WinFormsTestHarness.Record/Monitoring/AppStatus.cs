namespace WinFormsTestHarness.Record.Monitoring;

/// <summary>
/// 対象アプリケーションの状態。
/// </summary>
public enum AppStatus
{
    /// <summary>正常応答中</summary>
    Responsive,

    /// <summary>応答なし（ハング状態）</summary>
    Hung,

    /// <summary>プロセスが終了した</summary>
    Exited,
}
