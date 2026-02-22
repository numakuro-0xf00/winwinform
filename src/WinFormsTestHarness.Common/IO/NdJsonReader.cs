using WinFormsTestHarness.Common.Serialization;

namespace WinFormsTestHarness.Common.IO;

/// <summary>
/// NDJSON（改行区切りJSON）リーダー。
/// stdin またはファイルから1行ずつ読み込み、デシリアライズする。
/// 不正行は stderr に報告してスキップ（サイレントドロップ禁止）。
/// </summary>
public class NdJsonReader
{
    private readonly TextReader _reader;

    public NdJsonReader(TextReader reader)
    {
        _reader = reader;
    }

    /// <summary>stdin から読み込む NdJsonReader を生成</summary>
    public static NdJsonReader FromStdin() => new(Console.In);

    /// <summary>ファイルから読み込む NdJsonReader を生成</summary>
    public static NdJsonReader FromFile(string path)
        => new(new StreamReader(path, System.Text.Encoding.UTF8));

    /// <summary>
    /// 全行を読み込み、デシリアライズして返す。
    /// 不正行は stderr に報告してスキップする。
    /// </summary>
    public IEnumerable<T> ReadAll<T>()
    {
        int lineNumber = 0;
        string? line;
        while ((line = _reader.ReadLine()) != null)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            T? item;
            try
            {
                item = JsonHelper.Deserialize<T>(line);
            }
            catch (System.Text.Json.JsonException ex)
            {
                Console.Error.WriteLine($"Warning: NDJSON parse error at line {lineNumber}: {ex.Message}");
                Console.Error.WriteLine($"  Content: {Truncate(line, 200)}");
                continue;
            }

            if (item != null)
                yield return item;
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
