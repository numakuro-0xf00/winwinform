using WinFormsTestHarness.Common.Serialization;
using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Correlate.Readers;

/// <summary>
/// wfth-inspect watch 出力 NDJSON を読み込み、タイムスタンプでソートされたスナップショット一覧を返す。
/// </summary>
public static class UiaEventReader
{
    public static List<UiaChangeEvent> ReadAll(string path)
    {
        var events = new List<UiaChangeEvent>();
        int lineNumber = 0;

        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var ev = JsonHelper.Deserialize<UiaChangeEvent>(line);
                if (ev != null)
                    events.Add(ev);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: UIA NDJSON parse error at line {lineNumber}: {ex.Message}");
            }
        }

        events.Sort((a, b) => string.Compare(a.Ts, b.Ts, StringComparison.Ordinal));
        return events;
    }
}
