namespace WinFormsTestHarness.Record.Monitoring;

/// <summary>
/// アプリケーション健全性チェックの抽象化。
/// </summary>
public interface IAppHealthApi
{
    /// <summary>プロセスが存在するか</summary>
    bool IsProcessAlive(uint pid);

    /// <summary>ウィンドウがメッセージに応答するか（タイムアウト付き）</summary>
    bool IsWindowResponsive(IntPtr hwnd);
}
