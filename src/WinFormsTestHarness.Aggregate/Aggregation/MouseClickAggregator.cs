using WinFormsTestHarness.Aggregate.Models;
using WinFormsTestHarness.Common.IO;

namespace WinFormsTestHarness.Aggregate.Aggregation;

/// <summary>
/// 生マウスイベントを状態機械で集約し、Click / DoubleClick / RightClick / DragAndDrop / WheelScroll に変換する。
/// </summary>
public class MouseClickAggregator
{
    private enum State { Idle, PendingUp, PendingClick, Dragging, PendingDoubleClickUp }

    private readonly NdJsonWriter _writer;
    private readonly int _clickTimeoutMs;
    private readonly int _dblclickTimeoutMs;

    private State _state = State.Idle;

    // PendingUp / Dragging: the original down event
    private RawEvent? _downEvent;

    // PendingClick: stored click info
    private RawEvent? _clickUpEvent;

    // RightClick: simple flag
    private RawEvent? _rightDownEvent;

    public MouseClickAggregator(NdJsonWriter writer, int clickTimeoutMs = 300, int dblclickTimeoutMs = 500)
    {
        _writer = writer;
        _clickTimeoutMs = clickTimeoutMs;
        _dblclickTimeoutMs = dblclickTimeoutMs;
    }

    public void Process(RawEvent evt)
    {
        if (evt.Type != "mouse")
        {
            // Non-mouse event: flush pending click if any, then pass through
            if (_state == State.PendingClick)
            {
                OutputClick(_clickUpEvent!);
                ResetState();
            }
            return;
        }

        CheckTimeout(evt.Ts);
        HandleMouseEvent(evt);
    }

    public void CheckTimeout(DateTimeOffset currentTs)
    {
        switch (_state)
        {
            case State.PendingUp:
                if (_downEvent != null && (currentTs - _downEvent.Ts).TotalMilliseconds >= _clickTimeoutMs)
                {
                    _writer.WriteRaw(_downEvent.RawJson);
                    ResetState();
                }
                break;

            case State.PendingClick:
                if (_clickUpEvent != null && (currentTs - _clickUpEvent.Ts).TotalMilliseconds >= _dblclickTimeoutMs)
                {
                    OutputClick(_clickUpEvent);
                    ResetState();
                }
                break;
        }
    }

    public void Flush()
    {
        switch (_state)
        {
            case State.PendingUp:
            case State.Dragging:
                if (_downEvent != null)
                    _writer.WriteRaw(_downEvent.RawJson);
                break;

            case State.PendingClick:
                if (_clickUpEvent != null)
                    OutputClick(_clickUpEvent);
                break;

            case State.PendingDoubleClickUp:
                // 1st click confirmed, 2nd incomplete → output Click + raw LeftDown
                if (_clickUpEvent != null)
                    OutputClick(_clickUpEvent);
                if (_downEvent != null)
                    _writer.WriteRaw(_downEvent.RawJson);
                break;
        }
        ResetState();
    }

    private void HandleMouseEvent(RawEvent evt)
    {
        // Wheel events are immediate
        if (evt.Action is "WheelUp" or "WheelDown")
        {
            _writer.Write(new WheelScrollAction
            {
                Ts = evt.TsString,
                Direction = evt.Action == "WheelUp" ? "Up" : "Down",
                Sx = evt.Sx ?? 0,
                Sy = evt.Sy ?? 0,
                Rx = evt.Rx ?? 0,
                Ry = evt.Ry ?? 0,
            });
            return;
        }

        // Right-click: immediate pair matching
        if (evt.Action == "RightDown")
        {
            _rightDownEvent = evt;
            return;
        }

        if (evt.Action == "RightUp")
        {
            var down = _rightDownEvent ?? evt;
            _writer.Write(new RightClickAction
            {
                Ts = down.TsString,
                Sx = down.Sx ?? 0,
                Sy = down.Sy ?? 0,
                Rx = down.Rx ?? 0,
                Ry = down.Ry ?? 0,
            });
            _rightDownEvent = null;
            return;
        }

        // Left-button state machine
        switch (_state)
        {
            case State.Idle:
                if (evt.Action == "LeftDown")
                {
                    _downEvent = evt;
                    _state = State.PendingUp;
                }
                break;

            case State.PendingUp:
                if (evt.Action == "LeftUp")
                {
                    if ((evt.Ts - _downEvent!.Ts).TotalMilliseconds < _clickTimeoutMs)
                    {
                        // Click candidate
                        _clickUpEvent = _downEvent;
                        _state = State.PendingClick;
                    }
                    else
                    {
                        // Too slow for click, pass through raw
                        _writer.WriteRaw(_downEvent!.RawJson);
                        _writer.WriteRaw(evt.RawJson);
                        ResetState();
                    }
                }
                else if (evt.Action == "Move")
                {
                    _state = State.Dragging;
                }
                break;

            case State.PendingClick:
                if (evt.Action == "LeftDown" &&
                    (evt.Ts - _clickUpEvent!.Ts).TotalMilliseconds < _dblclickTimeoutMs)
                {
                    _downEvent = evt;
                    _state = State.PendingDoubleClickUp;
                }
                else
                {
                    OutputClick(_clickUpEvent!);
                    ResetState();
                    // Re-process this event in Idle state
                    if (evt.Action == "LeftDown")
                    {
                        _downEvent = evt;
                        _state = State.PendingUp;
                    }
                }
                break;

            case State.PendingDoubleClickUp:
                if (evt.Action == "LeftUp")
                {
                    _writer.Write(new DoubleClickAction
                    {
                        Ts = _clickUpEvent!.TsString,
                        Sx = _clickUpEvent.Sx ?? 0,
                        Sy = _clickUpEvent.Sy ?? 0,
                        Rx = _clickUpEvent.Rx ?? 0,
                        Ry = _clickUpEvent.Ry ?? 0,
                    });
                    ResetState();
                }
                break;

            case State.Dragging:
                if (evt.Action == "LeftUp")
                {
                    _writer.Write(new DragAndDropAction
                    {
                        Ts = _downEvent!.TsString,
                        StartSx = _downEvent.Sx ?? 0,
                        StartSy = _downEvent.Sy ?? 0,
                        StartRx = _downEvent.Rx ?? 0,
                        StartRy = _downEvent.Ry ?? 0,
                        EndSx = evt.Sx ?? 0,
                        EndSy = evt.Sy ?? 0,
                        EndRx = evt.Rx ?? 0,
                        EndRy = evt.Ry ?? 0,
                    });
                    ResetState();
                }
                break;
        }
    }

    private void OutputClick(RawEvent evt)
    {
        _writer.Write(new ClickAction
        {
            Ts = evt.TsString,
            Sx = evt.Sx ?? 0,
            Sy = evt.Sy ?? 0,
            Rx = evt.Rx ?? 0,
            Ry = evt.Ry ?? 0,
        });
    }

    private void ResetState()
    {
        _state = State.Idle;
        _downEvent = null;
        _clickUpEvent = null;
    }
}
