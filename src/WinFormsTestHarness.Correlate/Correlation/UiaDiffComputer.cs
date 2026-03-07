using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Correlate.Correlation;

public static class UiaDiffComputer
{
    public static UiaDiff Compute(UiaSnapshot? before, UiaSnapshot? after)
    {
        var diff = new UiaDiff();

        var beforeNodes = Flatten(before);
        var afterNodes = Flatten(after);

        var beforeKeys = new HashSet<string>(beforeNodes.Keys);
        var afterKeys = new HashSet<string>(afterNodes.Keys);

        // Added: in after but not in before
        foreach (var key in afterKeys.Except(beforeKeys))
        {
            var node = afterNodes[key];
            diff.Added.Add(new UiaDiffEntry
            {
                AutomationId = node.AutomationId,
                Name = node.Name,
                ControlType = node.ControlType
            });
        }

        // Removed: in before but not in after
        foreach (var key in beforeKeys.Except(afterKeys))
        {
            var node = beforeNodes[key];
            diff.Removed.Add(new UiaDiffEntry
            {
                AutomationId = node.AutomationId,
                Name = node.Name,
                ControlType = node.ControlType
            });
        }

        // Changed: in both but with different properties
        foreach (var key in beforeKeys.Intersect(afterKeys))
        {
            var b = beforeNodes[key];
            var a = afterNodes[key];

            if (b.Name != a.Name)
            {
                diff.Changed.Add(new UiaDiffChange
                {
                    AutomationId = a.AutomationId,
                    Property = "name",
                    From = b.Name,
                    To = a.Name
                });
            }

            var bSummary = FormatSummary(b.Summary);
            var aSummary = FormatSummary(a.Summary);
            if (bSummary != aSummary)
            {
                diff.Changed.Add(new UiaDiffChange
                {
                    AutomationId = a.AutomationId,
                    Property = "summary",
                    From = bSummary,
                    To = aSummary
                });
            }
        }

        return diff;
    }

    public static bool IsEmpty(UiaDiff diff)
        => diff.Added.Count == 0 && diff.Removed.Count == 0 && diff.Changed.Count == 0;

    private static Dictionary<string, FlatNode> Flatten(UiaSnapshot? snapshot)
    {
        var result = new Dictionary<string, FlatNode>();
        if (snapshot == null) return result;

        // Add root
        var rootKey = MakeKey(snapshot.AutomationId, snapshot.ControlType, "root");
        result[rootKey] = new FlatNode(snapshot.AutomationId, snapshot.Name, snapshot.ControlType, snapshot.Summary);

        // Flatten children
        FlattenChildren(snapshot.Children, "", result);
        return result;
    }

    private static void FlattenChildren(List<UiaNodeModel>? nodes, string parentPath, Dictionary<string, FlatNode> result)
    {
        if (nodes == null) return;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var path = $"{parentPath}/{i}";
            var key = MakeKey(node.AutomationId, node.ControlType, path);

            result[key] = new FlatNode(node.AutomationId, node.Name, node.ControlType, node.Summary);
            FlattenChildren(node.Children, path, result);
        }
    }

    private static string MakeKey(string? automationId, string? controlType, string treePath)
    {
        if (!string.IsNullOrEmpty(automationId))
            return $"{automationId}|{controlType}";
        return $"path:{treePath}|{controlType}";
    }

    private static string? FormatSummary(UiaSummaryModel? summary)
    {
        if (summary == null) return null;
        return $"rows={summary.Rows},columns={summary.Columns}";
    }

    private record FlatNode(string? AutomationId, string? Name, string? ControlType, UiaSummaryModel? Summary);
}
