using System.Windows.Forms;

namespace WinFormsTestHarness.Logger.Internal;

/// <summary>
/// パスワードフィールドの検出。
/// TextBox.PasswordChar または UseSystemPasswordChar が設定されているかを判定する。
/// </summary>
internal static class PasswordDetector
{
    internal static bool IsPasswordField(Control control)
    {
        if (control is TextBox textBox)
        {
            return textBox.PasswordChar != '\0' || textBox.UseSystemPasswordChar;
        }
        return false;
    }
}
