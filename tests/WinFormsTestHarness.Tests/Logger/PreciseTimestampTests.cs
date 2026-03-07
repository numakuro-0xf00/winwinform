using System.Text.RegularExpressions;
using NUnit.Framework;
using WinFormsTestHarness.Logger.Internal;

namespace WinFormsTestHarness.Tests.Logger;

[TestFixture]
public class PreciseTimestampTests
{
    [Test]
    public void Now_ISO8601形式の文字列を返す()
    {
        var ts = new PreciseTimestamp();

        var result = ts.Now();

        var pattern = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{6}Z$";
        Assert.That(Regex.IsMatch(result, pattern), Is.True,
            $"タイムスタンプ '{result}' が ISO 8601 マイクロ秒精度形式に一致しない");
    }

    [Test]
    public void Now_連続呼び出しでタイムスタンプが非減少()
    {
        var ts = new PreciseTimestamp();

        var t1 = ts.Now();
        var t2 = ts.Now();

        Assert.That(string.Compare(t2, t1, StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0),
            $"t2 ({t2}) は t1 ({t1}) 以上であるべき");
    }

    [Test]
    public void Now_現在時刻に近い値を返す()
    {
        // Arrange: before/after ブラケットで非決定性を軽減
        var before = DateTime.UtcNow;

        // Act
        var ts = new PreciseTimestamp();
        var result = ts.Now();
        var after = DateTime.UtcNow;

        // Assert
        var parsed = DateTime.ParseExact(result, "yyyy-MM-ddTHH:mm:ss.ffffffZ",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

        Assert.That(parsed, Is.GreaterThanOrEqualTo(before.AddSeconds(-5)),
            "タイムスタンプは計測開始前の時刻 - マージン以降であるべき");
        Assert.That(parsed, Is.LessThanOrEqualTo(after.AddSeconds(5)),
            "タイムスタンプは計測終了後の時刻 + マージン以内であるべき");
    }
}
