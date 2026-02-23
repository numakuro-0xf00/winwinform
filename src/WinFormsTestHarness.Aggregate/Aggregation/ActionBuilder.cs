using System.Text.Json;
using WinFormsTestHarness.Aggregate.Models;
using WinFormsTestHarness.Common.Cli;
using WinFormsTestHarness.Common.IO;
using WinFormsTestHarness.Common.Serialization;

namespace WinFormsTestHarness.Aggregate.Aggregation;

/// <summary>
/// MouseClickAggregator と KeySequenceAggregator を統合し、
/// stdin から NDJSON を読み込んで集約済みアクションを stdout に出力する。
/// screenshot / session / system / window イベントはパススルー。
/// </summary>
public class ActionBuilder
{
    private static readonly HashSet<string> PassthroughTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "screenshot", "session", "system", "window",
    };

    private readonly MouseClickAggregator _mouseAggregator;
    private readonly KeySequenceAggregator _keyAggregator;
    private readonly DiagnosticContext _diag;

    public ActionBuilder(
        int clickTimeoutMs = 300,
        int dblclickTimeoutMs = 500,
        int textTimeoutMs = 500,
        DiagnosticContext? diag = null)
    {
        _mouseAggregator = new MouseClickAggregator(clickTimeoutMs, dblclickTimeoutMs);
        _keyAggregator = new KeySequenceAggregator(textTimeoutMs);
        _diag = diag ?? new DiagnosticContext(false, true);
    }

    /// <summary>
    /// TextReader から NDJSON を読み込み、集約済みアクションを TextWriter に出力する。
    /// </summary>
    public void Process(TextReader input, TextWriter output)
    {
        var writer = new NdJsonWriter(output);
        int lineNumber = 0;

        string? line;
        while ((line = input.ReadLine()) != null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(line);
                root = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                _diag.Warn($"NDJSON parse error at line {lineNumber}: {ex.Message}");
                continue;
            }

            if (!root.TryGetProperty("type", out var typeProp))
            {
                _diag.Warn($"Missing 'type' field at line {lineNumber}");
                continue;
            }

            var type = typeProp.GetString();
            var ts = root.TryGetProperty("ts", out var tsProp) ? tsProp.GetString() : null;

            // 両アグリゲーターのタイムアウトチェック
            if (ts != null)
            {
                foreach (var action in _mouseAggregator.CheckTimeouts(ts))
                    writer.Write(action);
                foreach (var action in _keyAggregator.CheckTimeouts(ts))
                    writer.Write(action);
            }

            switch (type)
            {
                case "mouse":
                    ProcessMouseEvent(line, writer);
                    break;

                case "key":
                    ProcessKeyEvent(line, writer);
                    break;

                default:
                    if (type != null && PassthroughTypes.Contains(type))
                    {
                        // パススルー: 元の JSON 行をそのまま出力
                        output.WriteLine(line);
                        output.Flush();
                    }
                    else
                    {
                        _diag.DebugLog($"Unknown event type '{type}' at line {lineNumber}, passing through");
                        output.WriteLine(line);
                        output.Flush();
                    }
                    break;
            }
        }

        // EOF: 残りバッファをフラッシュ
        foreach (var action in _keyAggregator.Flush())
            writer.Write(action);
        foreach (var action in _mouseAggregator.Flush())
            writer.Write(action);
    }

    private void ProcessMouseEvent(string line, NdJsonWriter writer)
    {
        var mouseEvent = JsonHelper.Deserialize<RawMouseEvent>(line);
        if (mouseEvent == null) return;

        // マウスクリック座標をキーアグリゲーターのコンテキストに設定
        if (mouseEvent.Action is "LeftDown" or "LeftUp" or "RightDown" or "RightUp")
        {
            _keyAggregator.SetCoordinateContext(
                mouseEvent.Sx, mouseEvent.Sy, mouseEvent.Rx, mouseEvent.Ry);
        }

        foreach (var action in _mouseAggregator.ProcessEvent(mouseEvent))
            writer.Write(action);
    }

    private void ProcessKeyEvent(string line, NdJsonWriter writer)
    {
        var keyEvent = JsonHelper.Deserialize<RawKeyEvent>(line);
        if (keyEvent == null) return;

        foreach (var action in _keyAggregator.ProcessEvent(keyEvent))
            writer.Write(action);
    }
}
