using System.Text;
using WinFormsTestHarness.Aggregate.Models;
using WinFormsTestHarness.Common.IO;

namespace WinFormsTestHarness.Aggregate.Aggregation;

/// <summary>
/// 生キーイベントを TextInput / SpecialKey アクションに集約する。
/// 印字可能文字はバッファに蓄積し、タイムアウトまたは特殊キーで flush する。
/// </summary>
public class KeySequenceAggregator
{
    private static readonly HashSet<int> ModifierVks = new() { 16, 17, 18, 91, 92 };

    private static readonly Dictionary<int, string> SpecialKeyNames = new()
    {
        [13] = "Enter",
        [9] = "Tab",
        [27] = "Escape",
        [46] = "Delete",
        [8] = "Backspace",
        [112] = "F1", [113] = "F2", [114] = "F3", [115] = "F4",
        [116] = "F5", [117] = "F6", [118] = "F7", [119] = "F8",
        [120] = "F9", [121] = "F10", [122] = "F11", [123] = "F12",
        [37] = "Left", [38] = "Up", [39] = "Right", [40] = "Down",
        [36] = "Home", [35] = "End",
        [33] = "PageUp", [34] = "PageDown",
        [45] = "Insert",
    };

    private readonly NdJsonWriter _writer;
    private readonly int _textTimeoutMs;

    private readonly StringBuilder _buffer = new();
    private string _bufferStartTsString = "";
    private DateTimeOffset _lastEventTs;

    public KeySequenceAggregator(NdJsonWriter writer, int textTimeoutMs = 500)
    {
        _writer = writer;
        _textTimeoutMs = textTimeoutMs;
    }

    public void Process(RawEvent evt)
    {
        if (evt.Action != "down")
            return;

        var vk = evt.Vk;

        if (vk.HasValue && ModifierVks.Contains(vk.Value))
            return;

        // Check timeout before processing
        if (_buffer.Length > 0)
        {
            var elapsed = (evt.Ts - _lastEventTs).TotalMilliseconds;
            if (elapsed > _textTimeoutMs)
                FlushBuffer();
        }

        // Special key
        if (vk.HasValue && SpecialKeyNames.TryGetValue(vk.Value, out var keyName))
        {
            FlushBuffer();
            _writer.Write(new SpecialKeyAction
            {
                Ts = evt.TsString,
                Key = keyName,
                Vk = vk.Value,
            });
            return;
        }

        // Printable character
        if (evt.Char != null)
        {
            if (_buffer.Length == 0)
                _bufferStartTsString = evt.TsString;

            _buffer.Append(evt.Char);
            _lastEventTs = evt.Ts;
        }
    }

    public void CheckTimeout(DateTimeOffset currentTs)
    {
        if (_buffer.Length > 0)
        {
            var elapsed = (currentTs - _lastEventTs).TotalMilliseconds;
            if (elapsed > _textTimeoutMs)
                FlushBuffer();
        }
    }

    public void Flush()
    {
        FlushBuffer();
    }

    private void FlushBuffer()
    {
        if (_buffer.Length == 0)
            return;

        _writer.Write(new TextInputAction
        {
            Ts = _bufferStartTsString,
            Text = _buffer.ToString(),
        });
        _buffer.Clear();
    }
}
