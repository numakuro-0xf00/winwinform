using System.CommandLine;

namespace WinFormsTestHarness.Common.Cli;

/// <summary>
/// 全CLIツール共通のオプション定義。
/// --process, --hwnd, --out, --debug, --quiet を統一的に定義する。
/// </summary>
public static class CommonOptions
{
    public static Option<string?> Process() => new(
        "--process",
        description: "プロセス名で対象指定（部分一致、大文字小文字無視）");

    public static Option<string?> Hwnd() => new(
        "--hwnd",
        description: "ウィンドウハンドル（0xHHHH形式）");

    public static Option<string?> Output() => new(
        "--out",
        description: "出力ファイルパス（デフォルト: stdout）");

    public static Option<bool> Debug() => new(
        "--debug",
        getDefaultValue: () => false,
        description: "診断情報を stderr に出力");

    public static Option<bool> Quiet() => new(
        "--quiet",
        getDefaultValue: () => false,
        description: "stderr 出力を抑制（エラーのみ）");
}
