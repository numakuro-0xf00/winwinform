using NUnit.Framework;
using WinFormsTestHarness.Record.Hooks;

namespace WinFormsTestHarness.Tests.Record.Hooks;

[TestFixture]
public class KeyNameResolverTests
{
    [TestCase(0x41, "A")]
    [TestCase(0x5A, "Z")]
    [TestCase(0x30, "0")]
    [TestCase(0x39, "9")]
    public void Resolve_英数字キーはそのまま文字を返す(int vkCode, string expected)
    {
        Assert.That(KeyNameResolver.Resolve(vkCode), Is.EqualTo(expected));
    }

    [TestCase(0x0D, "Enter")]
    [TestCase(0x08, "Backspace")]
    [TestCase(0x09, "Tab")]
    [TestCase(0x1B, "Escape")]
    [TestCase(0x20, "Space")]
    public void Resolve_特殊キーは名前を返す(int vkCode, string expected)
    {
        Assert.That(KeyNameResolver.Resolve(vkCode), Is.EqualTo(expected));
    }

    [TestCase(0x70, "F1")]
    [TestCase(0x7B, "F12")]
    public void Resolve_ファンクションキーはF番号を返す(int vkCode, string expected)
    {
        Assert.That(KeyNameResolver.Resolve(vkCode), Is.EqualTo(expected));
    }

    [TestCase(0x60, "Numpad0")]
    [TestCase(0x69, "Numpad9")]
    public void Resolve_テンキーはNumpad番号を返す(int vkCode, string expected)
    {
        Assert.That(KeyNameResolver.Resolve(vkCode), Is.EqualTo(expected));
    }

    [Test]
    public void Resolve_未知のVKコードはVK_0xHH形式()
    {
        Assert.That(KeyNameResolver.Resolve(0xFF), Is.EqualTo("VK_0xFF"));
    }

    [TestCase(0x10, true)]  // Shift
    [TestCase(0x11, true)]  // Ctrl
    [TestCase(0x12, true)]  // Alt
    [TestCase(0x5B, true)]  // LWin
    [TestCase(0x41, false)] // A
    [TestCase(0x0D, false)] // Enter
    public void IsModifier_修飾キーの判定が正しい(int vkCode, bool expected)
    {
        Assert.That(KeyNameResolver.IsModifier(vkCode), Is.EqualTo(expected));
    }

    [TestCase(0x08, true)]  // Backspace
    [TestCase(0x25, true)]  // Left arrow
    [TestCase(0x70, true)]  // F1
    [TestCase(0x10, true)]  // Shift (modifier is also special)
    [TestCase(0x41, false)] // A
    [TestCase(0x30, false)] // 0
    public void IsSpecialKey_特殊キーの判定が正しい(int vkCode, bool expected)
    {
        Assert.That(KeyNameResolver.IsSpecialKey(vkCode), Is.EqualTo(expected));
    }
}
