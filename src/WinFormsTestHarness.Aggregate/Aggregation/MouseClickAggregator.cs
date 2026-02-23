using WinFormsTestHarness.Aggregate.Models;

namespace WinFormsTestHarness.Aggregate.Aggregation;

/// <summary>
/// マウスイベントの集約状態マシン。
/// LeftDown+Up → Click, 2x Click → DoubleClick, LeftDown+Move(drag)+Up → DragAndDrop,
/// RightDown+Up → RightClick, WheelUp/Down → WheelScroll に変換する。
/// </summary>
public class MouseClickAggregator
{
    private enum State { Idle, PendingUp, PendingRightUp, Dragging }

    private readonly int _clickTimeoutMs;
    private readonly int _dblclickTimeoutMs;

    private State _state = State.Idle;
    private RawMouseEvent? _pendingDown;
    private RawMouseEvent? _lastDragMove;
    private AggregatedAction? _pendingClick;

    public MouseClickAggregator(int clickTimeoutMs = 300, int dblclickTimeoutMs = 500)
    {
        _clickTimeoutMs = clickTimeoutMs;
        _dblclickTimeoutMs = dblclickTimeoutMs;
    }

    /// <summary>
    /// マウスイベントを処理し、集約済みアクションを 0 個以上返す。
    /// </summary>
    public IEnumerable<AggregatedAction> ProcessEvent(RawMouseEvent e)
    {
        // 現在時刻でタイムアウトチェック
        foreach (var action in CheckTimeouts(e.Ts!))
            yield return action;

        switch (_state)
        {
            case State.Idle:
                foreach (var action in HandleIdle(e))
                    yield return action;
                break;

            case State.PendingUp:
                foreach (var action in HandlePendingUp(e))
                    yield return action;
                break;

            case State.PendingRightUp:
                foreach (var action in HandlePendingRightUp(e))
                    yield return action;
                break;

            case State.Dragging:
                foreach (var action in HandleDragging(e))
                    yield return action;
                break;
        }
    }

    /// <summary>
    /// タイムスタンプに基づいてタイムアウトした保留状態をフラッシュする。
    /// </summary>
    public IEnumerable<AggregatedAction> CheckTimeouts(string? currentTs)
    {
        if (currentTs == null) yield break;

        // DoubleClick 待ちタイムアウト
        if (_pendingClick != null && DurationMs(_pendingClick.Ts!, currentTs) > _dblclickTimeoutMs)
        {
            yield return _pendingClick;
            _pendingClick = null;
        }

        // PendingUp タイムアウト（LeftDown 後に LeftUp が来ない）
        if (_state == State.PendingUp && _pendingDown != null &&
            DurationMs(_pendingDown.Ts!, currentTs) > _clickTimeoutMs)
        {
            // タイムアウト: LeftDown を単体イベントとしてフラッシュしない（後続 Up を待つ）
            // ただし click-timeout 超過なので Dragging 扱いへ遷移するか、
            // LeftUp が来たときに Click にならないようにする
            // → 状態はそのまま保持し、LeftUp 到着時に timeout 判定する
        }
    }

    /// <summary>
    /// 残りの保留状態をすべてフラッシュする（EOF 時に呼び出す）。
    /// </summary>
    public IEnumerable<AggregatedAction> Flush()
    {
        if (_pendingClick != null)
        {
            yield return _pendingClick;
            _pendingClick = null;
        }

        // PendingUp/Dragging の未完了状態はドロップ（不完全なジェスチャー）
        _state = State.Idle;
        _pendingDown = null;
        _lastDragMove = null;
    }

    private IEnumerable<AggregatedAction> HandleIdle(RawMouseEvent e)
    {
        switch (e.Action)
        {
            case "LeftDown":
                _state = State.PendingUp;
                _pendingDown = e;
                break;

            case "RightDown":
                _state = State.PendingRightUp;
                _pendingDown = e;
                break;

            case "WheelUp":
            case "WheelDown":
                yield return CreateWheelScroll(e);
                break;

            // Move, LeftUp, RightUp は Idle では無視
        }
    }

    private IEnumerable<AggregatedAction> HandlePendingUp(RawMouseEvent e)
    {
        switch (e.Action)
        {
            case "LeftUp":
                var duration = DurationMs(_pendingDown!.Ts!, e.Ts!);
                if (duration <= _clickTimeoutMs)
                {
                    // Click 候補
                    var click = CreateClick(_pendingDown!);
                    _state = State.Idle;
                    _pendingDown = null;

                    // DoubleClick チェック
                    if (_pendingClick != null &&
                        DurationMs(_pendingClick.Ts!, click.Ts!) <= _dblclickTimeoutMs)
                    {
                        // DoubleClick に昇格
                        _pendingClick.Type = "DoubleClick";
                        yield return _pendingClick;
                        _pendingClick = null;
                    }
                    else
                    {
                        // 前の pendingClick があれば先に出力
                        if (_pendingClick != null)
                        {
                            yield return _pendingClick;
                        }
                        _pendingClick = click;
                    }
                }
                else
                {
                    // click-timeout 超過 → Click にならない。
                    // Down + Up を個別イベントとしてフラッシュ（集約不可）
                    _state = State.Idle;
                    _pendingDown = null;
                }
                break;

            case "Move":
                if (e.Drag)
                {
                    _state = State.Dragging;
                    _lastDragMove = e;
                }
                break;

            case "WheelUp":
            case "WheelDown":
                // LeftDown 保留中にホイール → LeftDown は不完全、ドロップ
                _state = State.Idle;
                _pendingDown = null;
                yield return CreateWheelScroll(e);
                break;

            case "LeftDown":
                // 新たな LeftDown → 前の PendingUp を破棄してリセット
                _pendingDown = e;
                break;

            case "RightDown":
                // LeftDown 保留中に RightDown → LeftDown 破棄
                _state = State.PendingRightUp;
                _pendingDown = e;
                break;
        }
    }

    private IEnumerable<AggregatedAction> HandlePendingRightUp(RawMouseEvent e)
    {
        switch (e.Action)
        {
            case "RightUp":
                yield return CreateRightClick(_pendingDown!);
                _state = State.Idle;
                _pendingDown = null;
                break;

            case "LeftDown":
                // RightDown 保留中に LeftDown → RightDown 破棄
                _state = State.PendingUp;
                _pendingDown = e;
                break;

            default:
                // その他 → 保留状態を破棄
                _state = State.Idle;
                _pendingDown = null;
                break;
        }
    }

    private IEnumerable<AggregatedAction> HandleDragging(RawMouseEvent e)
    {
        switch (e.Action)
        {
            case "LeftUp":
                yield return CreateDragAndDrop(_pendingDown!, _lastDragMove ?? e);
                _state = State.Idle;
                _pendingDown = null;
                _lastDragMove = null;
                break;

            case "Move":
                _lastDragMove = e;
                break;

            default:
                // ドラッグ中に予期しないイベント → ドラッグ中断
                _state = State.Idle;
                _pendingDown = null;
                _lastDragMove = null;
                break;
        }
    }

    private static AggregatedAction CreateClick(RawMouseEvent down) => new()
    {
        Ts = down.Ts,
        Type = "Click",
        Button = "Left",
        Sx = down.Sx,
        Sy = down.Sy,
        Rx = down.Rx,
        Ry = down.Ry,
    };

    private static AggregatedAction CreateRightClick(RawMouseEvent down) => new()
    {
        Ts = down.Ts,
        Type = "RightClick",
        Sx = down.Sx,
        Sy = down.Sy,
        Rx = down.Rx,
        Ry = down.Ry,
    };

    private static AggregatedAction CreateDragAndDrop(RawMouseEvent down, RawMouseEvent end) => new()
    {
        Ts = down.Ts,
        Type = "DragAndDrop",
        StartSx = down.Sx,
        StartSy = down.Sy,
        EndSx = end.Sx,
        EndSy = end.Sy,
        StartRx = down.Rx,
        StartRy = down.Ry,
        EndRx = end.Rx,
        EndRy = end.Ry,
    };

    private static AggregatedAction CreateWheelScroll(RawMouseEvent e) => new()
    {
        Ts = e.Ts,
        Type = "WheelScroll",
        Direction = e.Action == "WheelUp" ? "Up" : "Down",
        Delta = e.Delta,
        Count = 1,
        Sx = e.Sx,
        Sy = e.Sy,
        Rx = e.Rx,
        Ry = e.Ry,
    };

    private static double DurationMs(string ts1, string ts2)
    {
        var t1 = DateTimeOffset.Parse(ts1, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var t2 = DateTimeOffset.Parse(ts2, null, System.Globalization.DateTimeStyles.RoundtripKind);
        return (t2 - t1).TotalMilliseconds;
    }
}
