using WinFormsTestHarness.Common.Serialization;

namespace WinFormsTestHarness.Common.IO;

/// <summary>
/// NDJSON（改行区切りJSON）ライター。
/// stdout または指定ファイルに1行1JSONオブジェクトで書き出す。
/// </summary>
public class NdJsonWriter : IDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;

    public NdJsonWriter(TextWriter writer, bool ownsWriter = false)
    {
        _writer = writer;
        _ownsWriter = ownsWriter;
    }

    /// <summary>stdout に書き出す NdJsonWriter を生成</summary>
    public static NdJsonWriter ToStdout() => new(Console.Out);

    /// <summary>ファイルに書き出す NdJsonWriter を生成</summary>
    public static NdJsonWriter ToFile(string path)
    {
        var stream = new StreamWriter(path, append: false, encoding: System.Text.Encoding.UTF8);
        return new NdJsonWriter(stream, ownsWriter: true);
    }

    public void Write<T>(T value)
    {
        _writer.WriteLine(JsonHelper.Serialize(value));
        _writer.Flush();
    }

    public void Dispose()
    {
        if (_ownsWriter)
            _writer.Dispose();
    }
}
