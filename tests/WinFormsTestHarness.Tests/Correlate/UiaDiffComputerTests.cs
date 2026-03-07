using NUnit.Framework;
using WinFormsTestHarness.Correlate.Correlation;
using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Tests.Correlate;

[TestFixture]
public class UiaDiffComputerTests
{
    [Test]
    public void 新規追加されたノードをaddedとして検出する()
    {
        var before = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:00Z",
            AutomationId = "MainForm",
            ControlType = "Window",
            Children = new List<UiaNodeModel>
            {
                new() { AutomationId = "btnSearch", Name = "検索", ControlType = "Button" }
            }
        };

        var after = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:01Z",
            AutomationId = "MainForm",
            ControlType = "Window",
            Children = new List<UiaNodeModel>
            {
                new() { AutomationId = "btnSearch", Name = "検索", ControlType = "Button" },
                new() { AutomationId = "txtResult", Name = "結果", ControlType = "Edit" }
            }
        };

        var diff = UiaDiffComputer.Compute(before, after);

        Assert.That(diff.Added, Has.Count.EqualTo(1));
        Assert.That(diff.Added[0].AutomationId, Is.EqualTo("txtResult"));
        Assert.That(diff.Removed, Is.Empty);
    }

    [Test]
    public void 削除されたノードをremovedとして検出する()
    {
        var before = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:00Z",
            AutomationId = "MainForm",
            ControlType = "Window",
            Children = new List<UiaNodeModel>
            {
                new() { AutomationId = "btnSearch", Name = "検索", ControlType = "Button" },
                new() { AutomationId = "txtResult", Name = "結果", ControlType = "Edit" }
            }
        };

        var after = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:01Z",
            AutomationId = "MainForm",
            ControlType = "Window",
            Children = new List<UiaNodeModel>
            {
                new() { AutomationId = "btnSearch", Name = "検索", ControlType = "Button" }
            }
        };

        var diff = UiaDiffComputer.Compute(before, after);

        Assert.That(diff.Removed, Has.Count.EqualTo(1));
        Assert.That(diff.Removed[0].AutomationId, Is.EqualTo("txtResult"));
        Assert.That(diff.Added, Is.Empty);
    }

    [Test]
    public void プロパティ変更をchangedとして検出する()
    {
        var before = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:00Z",
            AutomationId = "MainForm",
            ControlType = "Window",
            Children = new List<UiaNodeModel>
            {
                new()
                {
                    AutomationId = "dgvResults",
                    Name = "",
                    ControlType = "DataGrid",
                    Summary = new UiaSummaryModel { Rows = 5, Columns = 6 }
                }
            }
        };

        var after = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:01Z",
            AutomationId = "MainForm",
            ControlType = "Window",
            Children = new List<UiaNodeModel>
            {
                new()
                {
                    AutomationId = "dgvResults",
                    Name = "",
                    ControlType = "DataGrid",
                    Summary = new UiaSummaryModel { Rows = 1, Columns = 6 }
                }
            }
        };

        var diff = UiaDiffComputer.Compute(before, after);

        Assert.That(diff.Changed, Has.Count.EqualTo(1));
        Assert.That(diff.Changed[0].AutomationId, Is.EqualTo("dgvResults"));
        Assert.That(diff.Changed[0].Property, Is.EqualTo("summary"));
    }

    [Test]
    public void 両方nullの場合は空の差分を返す()
    {
        var diff = UiaDiffComputer.Compute(null, null);

        Assert.That(UiaDiffComputer.IsEmpty(diff), Is.True);
    }

    [Test]
    public void beforeがnullの場合はafter全体がaddedになる()
    {
        var after = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:00Z",
            AutomationId = "MainForm",
            ControlType = "Window",
            Children = new List<UiaNodeModel>
            {
                new() { AutomationId = "btn1", Name = "ボタン", ControlType = "Button" }
            }
        };

        var diff = UiaDiffComputer.Compute(null, after);

        Assert.That(diff.Added.Count, Is.GreaterThan(0));
        Assert.That(diff.Removed, Is.Empty);
    }

    [Test]
    public void 同一ツリーの場合は差分なし()
    {
        var snapshot = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:00Z",
            AutomationId = "MainForm",
            ControlType = "Window",
            Children = new List<UiaNodeModel>
            {
                new() { AutomationId = "btn1", Name = "ボタン", ControlType = "Button" }
            }
        };

        var diff = UiaDiffComputer.Compute(snapshot, snapshot);

        Assert.That(UiaDiffComputer.IsEmpty(diff), Is.True);
    }
}
