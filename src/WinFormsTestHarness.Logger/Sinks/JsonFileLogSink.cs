using System.Text.Json;
using System.Text.Json.Serialization;
using WinFormsTestHarness.Logger.Models;

namespace WinFormsTestHarness.Logger.Sinks;

/// <summary>
/// ローカルファイルへの NDJSON 出力 Sink。
/// フォールバック用途。ファイルサイズ上限に達したらローテーションする。
/// </summary>
internal sealed class JsonFileLogSink : ILogSink
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _baseFilePath;
    private readonly long _maxFileSize;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private long _currentFileSize;
    private int _rotationIndex;
    private string _currentFilePath;

    internal JsonFileLogSink(string? filePath, long maxFileSize)
    {
        _maxFileSize = maxFileSize;
        _baseFilePath = filePath ?? GenerateDefaultPath();
        _currentFilePath = _baseFilePath;

        EnsureDirectoryExists(_currentFilePath);
        _writer = new StreamWriter(_currentFilePath, append: false, encoding: System.Text.Encoding.UTF8)
        {
            AutoFlush = true,
        };
        _currentFileSize = 0;
    }

    public bool IsConnected => _writer != null;

    public void Write(LogEntry entry)
    {
        lock (_lock)
        {
            if (_writer == null) return;

            var json = JsonSerializer.Serialize(entry, s_jsonOptions);
            _writer.WriteLine(json);
            _currentFileSize += json.Length + Environment.NewLine.Length;

            if (_currentFileSize >= _maxFileSize)
            {
                Rotate();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Rotate()
    {
        _writer?.Dispose();
        _rotationIndex++;

        var dir = Path.GetDirectoryName(_baseFilePath)!;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(_baseFilePath);
        var ext = Path.GetExtension(_baseFilePath);
        _currentFilePath = Path.Combine(dir, $"{nameWithoutExt}.{_rotationIndex}{ext}");

        EnsureDirectoryExists(_currentFilePath);
        _writer = new StreamWriter(_currentFilePath, append: false, encoding: System.Text.Encoding.UTF8)
        {
            AutoFlush = true,
        };
        _currentFileSize = 0;
    }

    private static string GenerateDefaultPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WinFormsTestHarness", "logs");
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var pid = Environment.ProcessId;
        return Path.Combine(tempDir, $"applog_{timestamp}_{pid}.ndjson");
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
