using System.Windows.Forms;
using WinFormsTestHarness.Logger.Models;

namespace WinFormsTestHarness.Logger.Internal;

/// <summary>
/// コントロールの自動イベント監視。
/// 再帰的にコントロールツリーを走査し、型に応じたイベントハンドラを登録する。
/// </summary>
internal sealed class ControlWatcher : IDisposable
{
    private readonly LogPipeline _pipeline;
    private readonly PreciseTimestamp _timestamp;
    private readonly int _maxDepth;
    private readonly Dictionary<Control, List<(string EventName, Delegate Handler)>> _eventHandlers = new();
    private readonly Dictionary<Control, ControlInfo> _controlInfoCache = new();

    internal ControlWatcher(LogPipeline pipeline, PreciseTimestamp timestamp, int maxDepth)
    {
        _pipeline = pipeline;
        _timestamp = timestamp;
        _maxDepth = maxDepth;
    }

    internal void WatchRecursive(Control parent, int depth = 0)
    {
        if (depth > _maxDepth) return;

        WatchControl(parent);

        foreach (Control child in parent.Controls)
        {
            WatchRecursive(child, depth + 1);
        }

        parent.ControlAdded += OnControlAdded;
        parent.ControlRemoved += OnControlRemoved;
    }

    internal void UnwatchRecursive(Control parent)
    {
        parent.ControlAdded -= OnControlAdded;
        parent.ControlRemoved -= OnControlRemoved;

        foreach (Control child in parent.Controls)
        {
            UnwatchRecursive(child);
        }

        UnwatchControl(parent);
    }

    private void WatchControl(Control control)
    {
        if (_eventHandlers.ContainsKey(control)) return;

        var info = ControlInfo.FromControl(control);
        _controlInfoCache[control] = info;
        var handlers = new List<(string, Delegate)>();

        // 全コントロール共通イベント
        RegisterEvent(control, handlers, info, "Click",
            (EventHandler)((s, e) => LogEvent(info, "Click")));
        RegisterEvent(control, handlers, info, "GotFocus",
            (EventHandler)((s, e) => LogEvent(info, "GotFocus")));
        RegisterEvent(control, handlers, info, "VisibleChanged",
            (EventHandler)((s, e) => LogPropChange(info, "Visible", null, control.Visible, false)));
        RegisterEvent(control, handlers, info, "EnabledChanged",
            (EventHandler)((s, e) => LogPropChange(info, "Enabled", null, control.Enabled, false)));

        // 型固有イベント
        switch (control)
        {
            case TextBox textBox:
                RegisterEvent(control, handlers, info, "TextChanged",
                    (EventHandler)((s, e) => LogPropChange(info, "Text", null, textBox.Text, info.IsPasswordField)));
                RegisterEvent(control, handlers, info, "KeyDown",
                    (KeyEventHandler)((s, e) =>
                    {
                        if (e.KeyCode is Keys.Enter or Keys.Tab)
                            LogEvent(info, $"KeyDown:{e.KeyCode}");
                    }));
                break;
            case ComboBox comboBox:
                RegisterEvent(control, handlers, info, "SelectedIndexChanged",
                    (EventHandler)((s, e) => LogPropChange(info, "SelectedIndex", null, comboBox.SelectedItem, false)));
                break;
            case CheckBox checkBox:
                RegisterEvent(control, handlers, info, "CheckedChanged",
                    (EventHandler)((s, e) => LogPropChange(info, "Checked", null, checkBox.Checked, false)));
                break;
            case RadioButton radioButton:
                RegisterEvent(control, handlers, info, "CheckedChanged",
                    (EventHandler)((s, e) => LogPropChange(info, "Checked", null, radioButton.Checked, false)));
                break;
            case DataGridView dataGridView:
                RegisterEvent(control, handlers, info, "SelectionChanged",
                    (EventHandler)((s, e) => LogEvent(info, "SelectionChanged")));
                RegisterEvent(control, handlers, info, "CellClick",
                    (DataGridViewCellEventHandler)((s, e) =>
                    {
                        var entry = LogEntry.EventEntry(info, "CellClick", _timestamp.Now());
                        entry.Row = e.RowIndex;
                        _pipeline.Enqueue(entry);
                    }));
                break;
            case ListBox listBox:
                RegisterEvent(control, handlers, info, "SelectedIndexChanged",
                    (EventHandler)((s, e) => LogPropChange(info, "SelectedIndex", null, listBox.SelectedItem, false)));
                break;
            case NumericUpDown numericUpDown:
                RegisterEvent(control, handlers, info, "ValueChanged",
                    (EventHandler)((s, e) => LogPropChange(info, "Value", null, numericUpDown.Value, false)));
                break;
            case DateTimePicker dateTimePicker:
                RegisterEvent(control, handlers, info, "ValueChanged",
                    (EventHandler)((s, e) => LogPropChange(info, "Value", null, dateTimePicker.Value, false)));
                break;
            case TabControl tabControl:
                RegisterEvent(control, handlers, info, "SelectedIndexChanged",
                    (EventHandler)((s, e) => LogPropChange(info, "SelectedIndex", null, tabControl.SelectedIndex, false)));
                break;
        }

        _eventHandlers[control] = handlers;
    }

    private void UnwatchControl(Control control)
    {
        if (!_eventHandlers.TryGetValue(control, out var handlers)) return;

        foreach (var (eventName, handler) in handlers)
        {
            var eventInfo = control.GetType().GetEvent(eventName);
            eventInfo?.RemoveEventHandler(control, handler);
        }

        _eventHandlers.Remove(control);
        _controlInfoCache.Remove(control);
    }

    private void RegisterEvent(Control control, List<(string, Delegate)> handlers, ControlInfo info, string eventName, Delegate handler)
    {
        var eventInfo = control.GetType().GetEvent(eventName);
        if (eventInfo == null) return;

        eventInfo.AddEventHandler(control, handler);
        handlers.Add((eventName, handler));
    }

    private void LogEvent(ControlInfo info, string eventName)
    {
        _pipeline.Enqueue(LogEntry.EventEntry(info, eventName, _timestamp.Now()));
    }

    private void LogPropChange(ControlInfo info, string prop, object? old, object? @new, bool masked)
    {
        _pipeline.Enqueue(LogEntry.PropertyChanged(info, prop, old, @new, masked, _timestamp.Now()));
    }

    private void OnControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control != null)
            WatchControl(e.Control);
    }

    private void OnControlRemoved(object? sender, ControlEventArgs e)
    {
        if (e.Control != null)
            UnwatchControl(e.Control);
    }

    public void Dispose()
    {
        foreach (var (control, handlers) in _eventHandlers.ToArray())
        {
            foreach (var (eventName, handler) in handlers)
            {
                try
                {
                    var eventInfo = control.GetType().GetEvent(eventName);
                    eventInfo?.RemoveEventHandler(control, handler);
                }
                catch
                {
                    // コントロールが既に破棄されている場合は無視
                }
            }
        }
        _eventHandlers.Clear();
        _controlInfoCache.Clear();
    }
}
