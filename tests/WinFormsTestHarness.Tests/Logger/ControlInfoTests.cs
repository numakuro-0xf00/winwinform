using NUnit.Framework;
using WinFormsTestHarness.Logger.Models;

namespace WinFormsTestHarness.Tests.Logger;

[TestFixture]
public class ControlInfoTests
{
    [Test]
    public void コンストラクタ_全プロパティが設定される()
    {
        var info = new ControlInfo("btnOK", "Button", "MainForm", false);

        Assert.That(info.Name, Is.EqualTo("btnOK"));
        Assert.That(info.ControlTypeName, Is.EqualTo("Button"));
        Assert.That(info.FormName, Is.EqualTo("MainForm"));
        Assert.That(info.IsPasswordField, Is.False);
    }

    [Test]
    public void パスワードフィールドのフラグが正しく設定される()
    {
        var info = new ControlInfo("txtPassword", "TextBox", "LoginForm", true);

        Assert.That(info.IsPasswordField, Is.True);
    }

    [Test]
    public void フォーム名がnullでも正常に動作する()
    {
        var info = new ControlInfo("label1", "Label", null, false);

        Assert.That(info.FormName, Is.Null);
    }

    [Test]
    public void コンストラクタ_名前が空文字列でも正常に動作する()
    {
        var info = new ControlInfo("", "Button", "MainForm", false);

        Assert.That(info.Name, Is.EqualTo(""));
    }
}
