namespace WinFormsTestHarness.Record.Monitoring;

/// <summary>
/// フック生存確認用の入力プローブ抽象化。
/// SendInput で微小な入力を発生させ、フックの応答を確認する。
/// </summary>
public interface IProbeInput
{
    /// <summary>フック生存確認用の微小入力を発生させる</summary>
    void SendProbe();
}
