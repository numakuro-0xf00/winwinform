using System.Diagnostics;

namespace WinFormsTestHarness.Common.Timing;

/// <summary>
/// 高精度タイムスタンプ生成。
/// Stopwatch ベースの相対時間と DateTime.UtcNow の組み合わせで
/// ミリ秒精度のISO 8601タイムスタンプを提供する。
/// </summary>
public class PreciseTimestamp
{
    private readonly DateTime _baseUtc;
    private readonly long _baseElapsed;

    public PreciseTimestamp()
    {
        _baseUtc = DateTime.UtcNow;
        _baseElapsed = Stopwatch.GetTimestamp();
    }

    /// <summary>現在時刻を ISO 8601 UTC 文字列で返す</summary>
    public string Now()
    {
        var elapsed = Stopwatch.GetTimestamp() - _baseElapsed;
        var elapsedMs = (elapsed * 1000.0) / Stopwatch.Frequency;
        return _baseUtc.AddMilliseconds(elapsedMs).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }

    /// <summary>現在時刻を DateTime (UTC) で返す</summary>
    public DateTime NowUtc()
    {
        var elapsed = Stopwatch.GetTimestamp() - _baseElapsed;
        var elapsedMs = (elapsed * 1000.0) / Stopwatch.Frequency;
        return _baseUtc.AddMilliseconds(elapsedMs);
    }

    /// <summary>
    /// DateTime を ISO 8601 UTC 文字列に変換するユーティリティ
    /// </summary>
    public static string Format(DateTime utc)
        => utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
}
