using WinFormsTestHarness.Aggregate.Models;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.IO;

namespace WinFormsTestHarness.Aggregate.Aggregation;

/// <summary>
/// 生イベントストリームを読み込み、type でルーティングして集約する。
/// mouse → MouseClickAggregator, key → KeySequenceAggregator,
/// その他（session, screenshot, system, window）→ パススルー。
/// </summary>
public class ActionBuilder
{
    private readonly TextReader _input;
    private readonly NdJsonWriter _output;
    private readonly DiagnosticContext _diag;
    private readonly MouseClickAggregator _mouseAggregator;
    private readonly KeySequenceAggregator _keyAggregator;

    private static readonly HashSet<string> PassthroughTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "session", "screenshot", "system", "window"
    };

    public ActionBuilder(
        TextReader input,
        NdJsonWriter output,
        DiagnosticContext diag,
        int clickTimeoutMs = 300,
        int dblclickTimeoutMs = 500,
        int textTimeoutMs = 500)
    {
        _input = input;
        _output = output;
        _diag = diag;
        _mouseAggregator = new MouseClickAggregator(output, clickTimeoutMs, dblclickTimeoutMs);
        _keyAggregator = new KeySequenceAggregator(output, textTimeoutMs);
    }

    public int Run()
    {
        int lineNumber = 0;
        int processedCount = 0;
        int skippedCount = 0;
        string? line;

        while ((line = _input.ReadLine()) != null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var evt = RawEvent.Parse(line);
            if (evt == null)
            {
                _diag.Warn($"NDJSON parse error at line {lineNumber}");
                skippedCount++;
                continue;
            }

            // タイムアウトチェック（各イベント処理前）
            _mouseAggregator.CheckTimeout(evt.Ts);
            _keyAggregator.CheckTimeout(evt.Ts);

            switch (evt.Type)
            {
                case "mouse":
                    // マウスイベント前にキーバッファをフラッシュ
                    _keyAggregator.Flush();
                    _mouseAggregator.Process(evt);
                    break;

                case "key":
                    _keyAggregator.Process(evt);
                    break;

                default:
                    if (PassthroughTypes.Contains(evt.Type))
                    {
                        // パススルー前に両方フラッシュ
                        _mouseAggregator.Flush();
                        _keyAggregator.Flush();
                        _output.WriteRaw(evt.RawJson);
                    }
                    else
                    {
                        _diag.Warn($"Unknown event type '{evt.Type}' at line {lineNumber}, passing through");
                        _output.WriteRaw(evt.RawJson);
                    }
                    break;
            }

            processedCount++;
        }

        // EOF: 両アグリゲータをフラッシュ
        _mouseAggregator.Flush();
        _keyAggregator.Flush();

        _diag.Info($"wfth-aggregate: {processedCount} events processed, {skippedCount} skipped");
        return ExitCodes.Success;
    }
}
