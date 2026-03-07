using NUnit.Framework;
using WinFormsTestHarness.Correlate.Correlation;
using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Tests.Correlate;

[TestFixture]
public class UiaTargetResolverTests
{
    [Test]
    public void 座標がRect内の要素にヒットする()
    {
        var snapshot = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:00Z",
            Children = new List<UiaNodeModel>
            {
                new()
                {
                    AutomationId = "btnSearch",
                    Name = "検索",
                    ControlType = "Button",
                    Rect = new UiaRectModel(100, 100, 80, 30)
                }
            }
        };

        var result = UiaTargetResolver.Resolve(120, 110, snapshot);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Source, Is.EqualTo("UIA"));
        Assert.That(result.AutomationId, Is.EqualTo("btnSearch"));
        Assert.That(result.ControlType, Is.EqualTo("Button"));
    }

    [Test]
    public void 複数要素がヒットする場合は最小面積を選択する()
    {
        var snapshot = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:00Z",
            Children = new List<UiaNodeModel>
            {
                new()
                {
                    AutomationId = "panel",
                    Name = "パネル",
                    ControlType = "Pane",
                    Rect = new UiaRectModel(0, 0, 500, 400)
                },
                new()
                {
                    AutomationId = "btnSmall",
                    Name = "小ボタン",
                    ControlType = "Button",
                    Rect = new UiaRectModel(100, 100, 50, 25)
                }
            }
        };

        var result = UiaTargetResolver.Resolve(110, 105, snapshot);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.AutomationId, Is.EqualTo("btnSmall"));
    }

    [Test]
    public void 座標がどの要素にもヒットしない場合はnullを返す()
    {
        var snapshot = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:00Z",
            Children = new List<UiaNodeModel>
            {
                new()
                {
                    AutomationId = "btnSearch",
                    Name = "検索",
                    ControlType = "Button",
                    Rect = new UiaRectModel(100, 100, 80, 30)
                }
            }
        };

        var result = UiaTargetResolver.Resolve(500, 500, snapshot);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Rectがない要素はスキップされる()
    {
        var snapshot = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:00Z",
            Children = new List<UiaNodeModel>
            {
                new()
                {
                    AutomationId = "btnNoRect",
                    Name = "Rectなし",
                    ControlType = "Button"
                }
            }
        };

        var result = UiaTargetResolver.Resolve(110, 110, snapshot);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void snapshotがnullの場合はnullを返す()
    {
        var result = UiaTargetResolver.Resolve(100, 100, null);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ネストした子要素から最小面積を選択する()
    {
        var snapshot = new UiaSnapshot
        {
            Ts = "2026-01-01T00:00:00Z",
            Children = new List<UiaNodeModel>
            {
                new()
                {
                    AutomationId = "panel",
                    Name = "パネル",
                    ControlType = "Pane",
                    Rect = new UiaRectModel(0, 0, 500, 400),
                    Children = new List<UiaNodeModel>
                    {
                        new()
                        {
                            AutomationId = "btnNested",
                            Name = "ネストボタン",
                            ControlType = "Button",
                            Rect = new UiaRectModel(10, 10, 30, 20)
                        }
                    }
                }
            }
        };

        var result = UiaTargetResolver.Resolve(15, 15, snapshot);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.AutomationId, Is.EqualTo("btnNested"));
    }
}
