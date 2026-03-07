using System.Diagnostics;

namespace WinFormsTestHarness.Logger.Internal;

/// <summary>
/// 高精度タイムスタンプ生成（Logger 独自コピー）。
/// Logger は Common に依存しない独立パッケージのため複製。
/// Stopwatch ベースの相対時間と DateTime.UtcNow の組み合わせで
/// マイクロ秒精度の ISO 8601 タイムスタンプを提供する。
/// </summary>
internal sealed class PreciseTimestamp
{
    private readonly DateTime _baseUtc;
    private readonly long _baseElapsed;

    internal PreciseTimestamp()
    {
        _baseUtc = DateTime.UtcNow;
        _baseElapsed = Stopwatch.GetTimestamp();
    }

    /// <summary>現在時刻を ISO 8601 UTC 文字列で返す（マイクロ秒精度）</summary>
    internal string Now()
    {
        var elapsed = Stopwatch.GetTimestamp() - _baseElapsed;
        var elapsedTicks = (long)(elapsed * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency));
        return _baseUtc.AddTicks(elapsedTicks).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
    }
}
