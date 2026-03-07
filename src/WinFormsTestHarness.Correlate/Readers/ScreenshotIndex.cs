using System.Text.RegularExpressions;

namespace WinFormsTestHarness.Correlate.Readers;

public class ScreenshotIndex
{
    private readonly Dictionary<int, string> _beforeMap = new();
    private readonly Dictionary<int, string> _afterMap = new();

    private static readonly Regex FilePattern = new(@"^(\d+)_(before|after)\.png$", RegexOptions.Compiled);

    public ScreenshotIndex(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.GetFiles(directory, "*.png"))
        {
            var fileName = Path.GetFileName(file);
            var match = FilePattern.Match(fileName);
            if (!match.Success)
                continue;

            var seq = int.Parse(match.Groups[1].Value);
            var type = match.Groups[2].Value;

            if (type == "before")
                _beforeMap[seq] = file;
            else
                _afterMap[seq] = file;
        }
    }

    public string? GetBefore(int seq) => _beforeMap.GetValueOrDefault(seq);
    public string? GetAfter(int seq) => _afterMap.GetValueOrDefault(seq);
    public int Count => _beforeMap.Count + _afterMap.Count;
}
