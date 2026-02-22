namespace WinFormsTestHarness.Common.Cli;

/// <summary>
/// 全CLIツール共通の終了コード定義。
/// シェルスクリプト・CI連携で使用する。
/// </summary>
public static class ExitCodes
{
    /// <summary>正常終了</summary>
    public const int Success = 0;

    /// <summary>引数エラー（不正なオプション、必須引数不足）</summary>
    public const int ArgumentError = 1;

    /// <summary>対象未発見（プロセス、ウィンドウ、UI要素が見つからない）</summary>
    public const int TargetNotFound = 2;

    /// <summary>実行時エラー（UIA操作失敗、I/Oエラー等）</summary>
    public const int RuntimeError = 3;
}
