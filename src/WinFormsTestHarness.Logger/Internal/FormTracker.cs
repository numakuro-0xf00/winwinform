using System.Windows.Forms;
using WinFormsTestHarness.Logger.Models;

namespace WinFormsTestHarness.Logger.Internal;

/// <summary>
/// フォームの自動追跡。Application.Idle イベントで OpenForms を監視し、
/// 新しいフォームの検出・クローズの検知を行う。
/// </summary>
internal sealed class FormTracker : IDisposable
{
    private readonly LogPipeline _pipeline;
    private readonly ControlWatcher _controlWatcher;
    private readonly PreciseTimestamp _timestamp;
    private readonly HashSet<Form> _trackedForms = new();

    internal FormTracker(LogPipeline pipeline, ControlWatcher controlWatcher, PreciseTimestamp timestamp)
    {
        _pipeline = pipeline;
        _controlWatcher = controlWatcher;
        _timestamp = timestamp;
    }

    internal void Start()
    {
        Application.Idle += OnApplicationIdle;
    }

    internal void Stop()
    {
        Application.Idle -= OnApplicationIdle;
    }

    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        ScanOpenForms();
    }

    internal void ScanOpenForms()
    {
        try
        {
            foreach (Form form in Application.OpenForms)
            {
                if (_trackedForms.Contains(form)) continue;

                _trackedForms.Add(form);

                var ownerName = form.Owner?.Name;
                var modal = form.Modal;
                var formName = string.IsNullOrEmpty(form.Name) ? form.GetType().Name : form.Name;

                _pipeline.Enqueue(LogEntry.FormOpen(formName, ownerName, modal, _timestamp.Now()));

                _controlWatcher.WatchRecursive(form);

                form.FormClosed += (s, args) =>
                {
                    var closedForm = (Form)s!;
                    var closedName = string.IsNullOrEmpty(closedForm.Name) ? closedForm.GetType().Name : closedForm.Name;
                    _pipeline.Enqueue(LogEntry.FormClose(closedName, closedForm.DialogResult.ToString(), _timestamp.Now()));
                    _trackedForms.Remove(closedForm);
                    _controlWatcher.UnwatchRecursive(closedForm);
                };
            }
        }
        catch
        {
            // No-Throw: コレクション変更の競合を無視
        }
    }

    public void Dispose()
    {
        Stop();
        _trackedForms.Clear();
    }
}
