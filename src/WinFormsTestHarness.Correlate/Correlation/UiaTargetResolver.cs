using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Correlate.Correlation;

public static class UiaTargetResolver
{
    public static TargetElement? Resolve(int rx, int ry, UiaSnapshot? snapshot)
    {
        if (snapshot == null)
            return null;

        var best = FindSmallestContaining(rx, ry, snapshot.Children);
        if (best == null)
            return null;

        return new TargetElement
        {
            Source = "UIA",
            AutomationId = best.AutomationId,
            Name = best.Name,
            ControlType = best.ControlType,
            Rect = best.Rect,
            Description = $"AutomationId={best.AutomationId}, Name={best.Name}"
        };
    }

    private static UiaNodeModel? FindSmallestContaining(int rx, int ry, List<UiaNodeModel>? nodes)
    {
        if (nodes == null) return null;

        UiaNodeModel? best = null;
        int bestArea = int.MaxValue;

        foreach (var node in nodes)
        {
            if (node.Rect != null && Contains(node.Rect, rx, ry))
            {
                int area = node.Rect.W * node.Rect.H;
                if (area < bestArea)
                {
                    best = node;
                    bestArea = area;
                }
            }

            // Recurse into children
            var childBest = FindSmallestContaining(rx, ry, node.Children);
            if (childBest?.Rect != null)
            {
                int childArea = childBest.Rect.W * childBest.Rect.H;
                if (childArea < bestArea)
                {
                    best = childBest;
                    bestArea = childArea;
                }
            }
        }

        return best;
    }

    private static bool Contains(UiaRectModel rect, int x, int y)
        => x >= rect.X && x < rect.X + rect.W && y >= rect.Y && y < rect.Y + rect.H;
}
