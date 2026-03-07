using System.Drawing;
using System.Drawing.Imaging;

namespace WinFormsTestHarness.Capture;

/// <summary>
/// キャプチャ結果をファイルに保存する。
/// スレッドセーフな連番ファイル名を生成する。
/// </summary>
public class CaptureFileWriter
{
    private readonly string _outputDir;
    private int _sequence;

    public CaptureFileWriter(string outputDir)
    {
        _outputDir = outputDir;
    }

    /// <summary>
    /// CaptureResult の Bitmap をファイルに保存し、FilePath と FileSize を設定する。
    /// </summary>
    public void Save(CaptureResult result, string suffix)
    {
        if (result.Bitmap == null)
            return;

        Directory.CreateDirectory(_outputDir);

        var seq = Interlocked.Increment(ref _sequence);
        var fileName = $"{seq:D4}_{suffix}.png";
        var filePath = Path.Combine(_outputDir, fileName);

        result.Bitmap.Save(filePath, ImageFormat.Png);

        var fileInfo = new FileInfo(filePath);
        result.FilePath = filePath;
        result.FileSize = fileInfo.Length;
    }
}
