using WinFormsTestHarness.Common.IO;
using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Correlate.Readers;

public static class AppLogReader
{
    public static List<AppLogEntry> Read(string path)
    {
        using var stream = new StreamReader(path, System.Text.Encoding.UTF8);
        var reader = new NdJsonReader(stream);
        var entries = reader.ReadAll<AppLogEntry>().ToList();
        entries.Sort((a, b) => string.Compare(a.Ts, b.Ts, StringComparison.Ordinal));
        return entries;
    }
}
