using WinFormsTestHarness.Common.IO;
using WinFormsTestHarness.Correlate.Models;

namespace WinFormsTestHarness.Correlate.Readers;

public static class UiaSnapshotReader
{
    public static List<UiaSnapshot> Read(string path)
    {
        using var stream = new StreamReader(path, System.Text.Encoding.UTF8);
        var reader = new NdJsonReader(stream);
        var snapshots = reader.ReadAll<UiaSnapshot>().ToList();
        snapshots.Sort((a, b) => string.Compare(a.Ts, b.Ts, StringComparison.Ordinal));
        return snapshots;
    }
}
