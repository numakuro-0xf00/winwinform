using System.Text;
using WinFormsTestHarness.Aggregate.Models;

namespace WinFormsTestHarness.Aggregate.Aggregation;

/// <summary>
/// キーボードイベントの集約状態マシン。
/// 連続する印字可能キー入力を TextInput に、
/// 特殊キー（Enter, Tab, Escape 等）を SpecialKey に変換する。
/// </summary>
public class KeySequenceAggregator
{
    private static readonly HashSet<string> SpecialKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Enter", "Return", "Tab", "Escape", "Delete", "Backspace",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Home", "End", "PageUp", "PageDown",
        "Insert", "PrintScreen", "Pause",
        "Up", "Down", "Left", "Right",
    };

    private static readonly HashSet<string> ModifierKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shift", "LShift", "RShift",
        "Control", "LControl", "RControl",
        "Alt", "LAlt", "RAlt", "Menu", "LMenu", "RMenu",
        "LWin", "RWin",
    };

    private readonly int _textTimeoutMs;
    private readonly StringBuilder _textBuffer = new();
    private string? _firstTs;
    private string? _lastTs;
    private int? _lastSx;
    private int? _lastSy;
    private int? _lastRx;
    private int? _lastRy;

    public KeySequenceAggregator(int textTimeoutMs = 500)
    {
        _textTimeoutMs = textTimeoutMs;
    }

    /// <summary>
    /// 座標コンテキストを設定する（直前のマウスクリック位置）。
    /// TextInput / SpecialKey に付与するための座標。
    /// </summary>
    public void SetCoordinateContext(int sx, int sy, int rx, int ry)
    {
        _lastSx = sx;
        _lastSy = sy;
        _lastRx = rx;
        _lastRy = ry;
    }

    /// <summary>
    /// キーイベントを処理し、集約済みアクションを 0 個以上返す。
    /// </summary>
    public IEnumerable<AggregatedAction> ProcessEvent(RawKeyEvent e)
    {
        // key up は無視（テキスト入力は key down のみで判定）
        if (e.Action != "down") yield break;

        // タイムアウトチェック
        foreach (var action in CheckTimeouts(e.Ts!))
            yield return action;

        var key = e.Key;

        // 修飾キーは無視
        if (key != null && ModifierKeys.Contains(key))
            yield break;

        // 特殊キー → バッファフラッシュ + SpecialKey 出力
        if (key != null && SpecialKeys.Contains(key))
        {
            foreach (var action in FlushTextBuffer())
                yield return action;

            yield return CreateSpecialKey(e);
            yield break;
        }

        // 印字可能キー → バッファに追加
        var ch = e.Char;
        if (!string.IsNullOrEmpty(ch))
        {
            if (_textBuffer.Length == 0)
                _firstTs = e.Ts;
            _textBuffer.Append(ch);
            _lastTs = e.Ts;
        }
    }

    /// <summary>
    /// タイムスタンプに基づいてタイムアウトしたテキストバッファをフラッシュする。
    /// </summary>
    public IEnumerable<AggregatedAction> CheckTimeouts(string? currentTs)
    {
        if (currentTs == null || _textBuffer.Length == 0 || _lastTs == null)
            yield break;

        if (DurationMs(_lastTs, currentTs) > _textTimeoutMs)
        {
            foreach (var action in FlushTextBuffer())
                yield return action;
        }
    }

    /// <summary>
    /// 残りのバッファをフラッシュする（EOF 時に呼び出す）。
    /// </summary>
    public IEnumerable<AggregatedAction> Flush()
    {
        return FlushTextBuffer();
    }

    private IEnumerable<AggregatedAction> FlushTextBuffer()
    {
        if (_textBuffer.Length == 0) yield break;

        yield return new AggregatedAction
        {
            Ts = _firstTs,
            Type = "TextInput",
            Text = _textBuffer.ToString(),
            StartTs = _firstTs,
            EndTs = _lastTs,
            Sx = _lastSx,
            Sy = _lastSy,
            Rx = _lastRx,
            Ry = _lastRy,
        };

        _textBuffer.Clear();
        _firstTs = null;
        _lastTs = null;
    }

    private AggregatedAction CreateSpecialKey(RawKeyEvent e) => new()
    {
        Ts = e.Ts,
        Type = "SpecialKey",
        Key = e.Key,
        Sx = _lastSx,
        Sy = _lastSy,
        Rx = _lastRx,
        Ry = _lastRy,
    };

    private static double DurationMs(string ts1, string ts2)
    {
        var t1 = DateTimeOffset.Parse(ts1, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var t2 = DateTimeOffset.Parse(ts2, null, System.Globalization.DateTimeStyles.RoundtripKind);
        return (t2 - t1).TotalMilliseconds;
    }
}
