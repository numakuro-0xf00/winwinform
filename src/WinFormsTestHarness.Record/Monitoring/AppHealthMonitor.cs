namespace WinFormsTestHarness.Record.Monitoring;

/// <summary>
/// 対象アプリケーションの健全性監視。
/// プロセス存在とウィンドウ応答性を確認する。
/// </summary>
public class AppHealthMonitor
{
    private readonly IAppHealthApi _api;
    private readonly uint _targetPid;
    private readonly IntPtr _targetHwnd;

    public AppHealthMonitor(IAppHealthApi api, uint targetPid, IntPtr targetHwnd)
    {
        _api = api;
        _targetPid = targetPid;
        _targetHwnd = targetHwnd;
    }

    /// <summary>
    /// 対象アプリの状態を確認する。
    /// </summary>
    public AppStatus Check()
    {
        if (!_api.IsProcessAlive(_targetPid))
            return AppStatus.Exited;

        if (!_api.IsWindowResponsive(_targetHwnd))
            return AppStatus.Hung;

        return AppStatus.Responsive;
    }
}
