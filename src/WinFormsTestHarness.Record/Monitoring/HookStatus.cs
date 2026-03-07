namespace WinFormsTestHarness.Record.Monitoring;

/// <summary>
/// フック生存状態。
/// </summary>
public enum HookStatus
{
    /// <summary>フック稼働中、イベント受信中</summary>
    Alive,

    /// <summary>フック稼働中、アイドル（入力なし）</summary>
    AliveIdle,

    /// <summary>フック応答なし（再設定が必要な可能性）</summary>
    PossiblyDead,
}
