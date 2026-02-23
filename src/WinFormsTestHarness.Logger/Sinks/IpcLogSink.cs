using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinFormsTestHarness.Logger.Models;

namespace WinFormsTestHarness.Logger.Sinks;

/// <summary>
/// 名前付きパイプによる IPC 出力 Sink。
/// Recording Engine にログを送信する。接続失敗時は IsConnected=false となり、
/// LogPipeline がフォールバック Sink に切り替える。
/// </summary>
internal sealed class IpcLogSink : ILogSink
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string? _pipeName;
    private readonly int _connectTimeoutMs;
    private readonly int _reconnectIntervalMs;
    private readonly int _maxReconnectAttempts;
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private bool _connected;
    private int _reconnectAttempts;
    private DateTime _lastReconnectAttempt;

    internal IpcLogSink(LoggerConfig config)
    {
        _connectTimeoutMs = config.PipeConnectTimeoutMs;
        _reconnectIntervalMs = config.ReconnectIntervalMs;
        _maxReconnectAttempts = config.MaxReconnectAttempts;

        _pipeName = ResolvePipeName(config);
        if (_pipeName != null)
        {
            TryConnect();
        }
    }

    public bool IsConnected => _connected;

    public void Write(LogEntry entry)
    {
        if (!_connected)
        {
            TryReconnectIfDue();
            if (!_connected) throw new IOException("IPC not connected");
        }

        try
        {
            var json = JsonSerializer.Serialize(entry, s_jsonOptions);
            _writer!.WriteLine(json);
            _writer.Flush();
        }
        catch (IOException)
        {
            _connected = false;
            throw;
        }
    }

    public void Dispose()
    {
        _connected = false;
        _writer?.Dispose();
        _pipe?.Dispose();
        _writer = null;
        _pipe = null;
    }

    private void TryConnect()
    {
        try
        {
            _pipe = new NamedPipeClientStream(".", _pipeName!, PipeDirection.Out);
            _pipe.Connect(_connectTimeoutMs);
            _writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
            _connected = true;
        }
        catch
        {
            _connected = false;
            _pipe?.Dispose();
            _pipe = null;
            _writer = null;
        }
    }

    private void TryReconnectIfDue()
    {
        if (_pipeName == null) return;
        if (_reconnectAttempts >= _maxReconnectAttempts) return;

        var now = DateTime.UtcNow;
        if ((now - _lastReconnectAttempt).TotalMilliseconds < _reconnectIntervalMs) return;

        _lastReconnectAttempt = now;
        _reconnectAttempts++;

        _writer?.Dispose();
        _pipe?.Dispose();

        TryConnect();
    }

    private static string? ResolvePipeName(LoggerConfig config)
    {
        int? pid = config.RecordingEnginePid;
        if (pid == null)
        {
            var envPid = Environment.GetEnvironmentVariable("WFTH_RECORDER_PID");
            if (int.TryParse(envPid, out var p))
                pid = p;
        }

        if (pid == null) return null;

        var nonce = Environment.GetEnvironmentVariable("WFTH_SESSION_NONCE") ?? "default";
        return $"WinFormsTestHarness_{pid}_{nonce}";
    }
}
