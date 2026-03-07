using System.Text.Json;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.IO;
using WinFormsTestHarness.Common.Serialization;
using WinFormsTestHarness.Correlate.Models;
using WinFormsTestHarness.Correlate.Readers;

namespace WinFormsTestHarness.Correlate.Correlation;

public class TimeWindowCorrelator
{
    private readonly List<UiaSnapshot> _uiaSnapshots;
    private readonly List<AppLogEntry>? _appLogEntries;
    private readonly ScreenshotIndex? _screenshotIndex;
    private readonly int _windowMs;
    private readonly bool _includeNoise;
    private readonly NoiseClassifier _noiseClassifier;
    private readonly bool _explain;
    private readonly DiagnosticContext _diag;

    public TimeWindowCorrelator(
        List<UiaSnapshot> uiaSnapshots,
        List<AppLogEntry>? appLogEntries,
        ScreenshotIndex? screenshotIndex,
        int windowMs,
        bool includeNoise,
        double noiseThreshold,
        bool explain,
        DiagnosticContext diag)
    {
        _uiaSnapshots = uiaSnapshots;
        _appLogEntries = appLogEntries;
        _screenshotIndex = screenshotIndex;
        _windowMs = windowMs;
        _includeNoise = includeNoise;
        _noiseClassifier = new NoiseClassifier(noiseThreshold);
        _explain = explain;
        _diag = diag;
    }

    public void Execute(NdJsonReader stdin, NdJsonWriter stdout)
    {
        var allActions = stdin.ReadAll<AggregatedAction>().ToList();
        _diag.DebugLog($"入力アクション数: {allActions.Count}");

        // Separate screenshot metadata lines
        var actions = allActions.Where(a => a.Type != "screenshot").ToList();
        _diag.DebugLog($"処理対象アクション数: {actions.Count} (screenshot除外)");

        int seq = 0;
        int correlatedCount = 0;
        int noiseCount = 0;
        int uiaMatchCount = 0;
        int screenshotMatchCount = 0;
        int appLogMatchCount = 0;
        AggregatedAction? previousAction = null;

        foreach (var action in actions)
        {
            seq++;

            // System type → pass through with note
            if (action.Type == "system" || action.Type == "SystemGap")
            {
                var systemCorrelated = new CorrelatedAction
                {
                    Seq = seq,
                    Ts = action.Ts,
                    Type = action.Type,
                    Input = JsonSerializer.SerializeToElement(action, JsonHelper.Options),
                    Note = action.Message ?? "system event"
                };
                stdout.Write(systemCorrelated);
                continue;
            }

            // Find UIA snapshots in time window
            var (beforeSnapshot, afterSnapshot) = FindUiaSnapshots(action.Ts);

            // Target resolution
            TargetElement? target = null;
            if (action.Rx != null && action.Ry != null)
            {
                target = UiaTargetResolver.Resolve(action.Rx.Value, action.Ry.Value, beforeSnapshot);
                if (target == null)
                {
                    // Fallback to coordinate-only target
                    target = new TargetElement
                    {
                        Source = "coordinate",
                        Description = $"({action.Rx}, {action.Ry})"
                    };
                }
            }

            // UIA diff (only when both before and after exist)
            UiaDiff? uiaDiff = null;
            bool hasUiaDiff = false;
            if (beforeSnapshot != null && afterSnapshot != null)
            {
                uiaDiff = UiaDiffComputer.Compute(beforeSnapshot, afterSnapshot);
                hasUiaDiff = !UiaDiffComputer.IsEmpty(uiaDiff);
            }

            // Screenshot links
            var screenshots = ResolveScreenshots(seq);
            bool hasScreenshots = screenshots != null;

            // App log correlation
            var appLogs = FindAppLogs(action.Ts);
            bool hasAppLog = appLogs != null && appLogs.Count > 0;

            // Noise classification
            var noise = _noiseClassifier.Classify(action, uiaDiff, appLogs, previousAction);
            if (noise != null)
                noiseCount++;

            // Skip noise if not including
            if (noise != null && !_includeNoise)
            {
                _diag.DebugLog($"ノイズ除外: seq={seq}, reason={noise.Reason}, confidence={noise.Confidence}");
                previousAction = action;
                continue;
            }

            // Track metrics
            correlatedCount++;
            if (hasUiaDiff) uiaMatchCount++;
            if (hasScreenshots) screenshotMatchCount++;
            if (hasAppLog) appLogMatchCount++;

            // Build explain info
            ExplainInfo? explain = null;
            if (_explain)
            {
                explain = new ExplainInfo();
                if (beforeSnapshot != null || afterSnapshot != null)
                {
                    explain.UiaMatch = hasUiaDiff
                        ? $"UIA change detected within {_windowMs}ms window"
                        : $"No UIA change within {_windowMs}ms window";
                }
                if (screenshots != null)
                {
                    explain.ScreenshotMatch = $"before: {screenshots.Before ?? "none"}, after: {screenshots.After ?? "none"}";
                }
                if (target != null)
                {
                    explain.TargetSource = target.Source == "UIA"
                        ? $"UIA {target.AutomationId} matched by coordinate intersection"
                        : $"Coordinate fallback ({action.Rx}, {action.Ry})";
                }
                if (hasAppLog)
                {
                    explain.AppLogMatch = $"{appLogs!.Count} app log entries within {_windowMs}ms window";
                }
                if (noise != null)
                {
                    explain.NoiseReason = $"{noise.Reason} (confidence: {noise.Confidence})";
                }
            }

            var correlated = new CorrelatedAction
            {
                Seq = seq,
                Ts = action.Ts,
                Type = action.Type,
                Input = JsonSerializer.SerializeToElement(action, JsonHelper.Options),
                Target = target,
                Screenshots = screenshots,
                UiaDiff = hasUiaDiff ? uiaDiff : null,
                AppLog = hasAppLog ? appLogs : null,
                Noise = noise,
                Explain = explain
            };

            stdout.Write(correlated);
            previousAction = action;
        }

        // Write summary
        var summary = new CorrelationSummary
        {
            SummaryType = "correlation",
            Metrics = new CorrelationMetrics
            {
                TotalActions = seq,
                CorrelatedActions = correlatedCount,
                NoiseActions = noiseCount,
                UiaMatchRate = correlatedCount > 0 ? (double)uiaMatchCount / correlatedCount : 0,
                ScreenshotMatchRate = correlatedCount > 0 ? (double)screenshotMatchCount / correlatedCount : 0,
                AppLogMatchRate = correlatedCount > 0 ? (double)appLogMatchCount / correlatedCount : 0
            }
        };
        stdout.Write(summary);

        _diag.Info($"相関完了: {correlatedCount} アクション, {noiseCount} ノイズ");
    }

    private (UiaSnapshot? before, UiaSnapshot? after) FindUiaSnapshots(string actionTs)
    {
        if (_uiaSnapshots.Count == 0)
            return (null, null);

        // Parse action timestamp and compute window boundaries
        if (!DateTime.TryParse(actionTs, null, System.Globalization.DateTimeStyles.RoundtripKind, out var actionDt))
            return (null, null);

        var windowStart = actionDt.AddMilliseconds(-50);
        var windowEnd = actionDt.AddMilliseconds(_windowMs);

        UiaSnapshot? before = null;
        UiaSnapshot? after = null;

        foreach (var snapshot in _uiaSnapshots)
        {
            if (!DateTime.TryParse(snapshot.Ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var snapshotDt))
                continue;

            // Before: latest snapshot at or before action time
            if (snapshotDt <= actionDt)
                before = snapshot;

            // After: first snapshot after action time within window
            if (snapshotDt > actionDt && snapshotDt <= windowEnd && after == null)
                after = snapshot;
        }

        return (before, after);
    }

    private ScreenshotPaths? ResolveScreenshots(int seq)
    {
        if (_screenshotIndex == null)
            return null;

        var before = _screenshotIndex.GetBefore(seq);
        var after = _screenshotIndex.GetAfter(seq);

        if (before == null && after == null)
            return null;

        return new ScreenshotPaths { Before = before, After = after };
    }

    private List<AppLogEntry>? FindAppLogs(string actionTs)
    {
        if (_appLogEntries == null || _appLogEntries.Count == 0)
            return null;

        if (!DateTime.TryParse(actionTs, null, System.Globalization.DateTimeStyles.RoundtripKind, out var actionDt))
            return null;

        var windowEnd = actionDt.AddMilliseconds(_windowMs);
        var result = new List<AppLogEntry>();

        foreach (var entry in _appLogEntries)
        {
            if (!DateTime.TryParse(entry.Ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var entryDt))
                continue;

            if (entryDt >= actionDt && entryDt <= windowEnd)
                result.Add(entry);
        }

        return result.Count > 0 ? result : null;
    }
}
