namespace WinFormsTestHarness.Logger.Models;

/// <summary>
/// コントロールのメタデータスナップショット。
/// UI スレッドで1回だけ生成し、以降はイミュータブルとして使用する。
/// </summary>
internal sealed class ControlInfo
{
    public string Name { get; }
    public string ControlTypeName { get; }
    public string? FormName { get; }
    public bool IsPasswordField { get; }

    internal ControlInfo(string name, string controlTypeName, string? formName, bool isPasswordField)
    {
        Name = name;
        ControlTypeName = controlTypeName;
        FormName = formName;
        IsPasswordField = isPasswordField;
    }

    /// <summary>
    /// WinForms Control からスナップショットを生成する。
    /// 名前なしコントロールには _{TypeName}_{HashCode:X8} 形式のフォールバック名を付与。
    /// </summary>
    internal static ControlInfo FromControl(System.Windows.Forms.Control control)
    {
        var name = string.IsNullOrEmpty(control.Name)
            ? $"_{control.GetType().Name}_{control.GetHashCode():X8}"
            : control.Name;

        var typeName = control.GetType().Name;

        string? formName = null;
        var form = control.FindForm();
        if (form != null)
            formName = form.Name;

        var isPassword = Internal.PasswordDetector.IsPasswordField(control);

        return new ControlInfo(name, typeName, formName, isPassword);
    }
}
