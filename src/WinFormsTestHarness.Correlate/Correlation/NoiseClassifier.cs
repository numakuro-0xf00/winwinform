using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Correlate.Correlation;

public class NoiseClassifier
{
    private readonly double _threshold;

    public NoiseClassifier(double threshold = 0.7)
    {
        _threshold = threshold;
    }

    public NoiseClassification? Classify(
        AggregatedAction action,
        UiaDiff? uiaDiff,
        List<AppLogEntry>? appLogs,
        AggregatedAction? previousAction)
    {
        NoiseClassification? result = null;

        // empty_click: Click with no UIA change and no app log
        if (action.Type == "Click" &&
            (uiaDiff == null || UiaDiffComputer.IsEmpty(uiaDiff)) &&
            (appLogs == null || appLogs.Count == 0))
        {
            result = new NoiseClassification { Reason = "empty_click", Confidence = 0.8 };
        }

        // duplicate_click: same coordinates within 500ms of previous action
        if (action.Type == "Click" && previousAction?.Type == "Click" &&
            action.Rx == previousAction.Rx && action.Ry == previousAction.Ry &&
            IsWithinMs(previousAction.Ts, action.Ts, 500))
        {
            var candidate = new NoiseClassification { Reason = "duplicate_click", Confidence = 0.9 };
            if (result == null || candidate.Confidence > result.Confidence)
                result = candidate;
        }

        // accidental_drag: DragAndDrop with distance < 5px
        if (action.Type == "DragAndDrop" &&
            action.Rx != null && action.Ry != null &&
            action.EndRx != null && action.EndRy != null)
        {
            var dx = action.EndRx.Value - action.Rx.Value;
            var dy = action.EndRy.Value - action.Ry.Value;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 5)
            {
                var candidate = new NoiseClassification { Reason = "accidental_drag", Confidence = 0.85 };
                if (result == null || candidate.Confidence > result.Confidence)
                    result = candidate;
            }
        }

        // Return only if above threshold
        if (result != null && result.Confidence >= _threshold)
            return result;

        return null;
    }

    private static bool IsWithinMs(string ts1, string ts2, int ms)
    {
        if (!DateTime.TryParse(ts1, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt1) ||
            !DateTime.TryParse(ts2, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt2))
            return false;

        return Math.Abs((dt2 - dt1).TotalMilliseconds) <= ms;
    }
}
