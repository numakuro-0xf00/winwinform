using NUnit.Framework;
using WinFormsTestHarness.Logger;

namespace WinFormsTestHarness.Tests.Logger;

[TestFixture]
public class LoggerConfigTests
{
    [Test]
    public void Default_全プロパティにデフォルト値が設定されている()
    {
        var config = LoggerConfig.Default;

        Assert.That(config.RecordingEnginePid, Is.Null);
        Assert.That(config.PipeConnectTimeoutMs, Is.EqualTo(3000));
        Assert.That(config.MaxQueueSize, Is.EqualTo(10000));
        Assert.That(config.FlushIntervalMs, Is.EqualTo(100));
        Assert.That(config.FallbackFilePath, Is.Null);
        Assert.That(config.MaxFallbackFileSizeBytes, Is.EqualTo(50 * 1024 * 1024));
        Assert.That(config.ReconnectIntervalMs, Is.EqualTo(5000));
        Assert.That(config.MaxReconnectAttempts, Is.EqualTo(10));
        Assert.That(config.AutoWatchControls, Is.True);
        Assert.That(config.TrackForms, Is.True);
        Assert.That(config.MaxControlDepth, Is.EqualTo(20));
    }

    [Test]
    public void Default_呼び出しごとに新しいインスタンスを返す()
    {
        var config1 = LoggerConfig.Default;
        var config2 = LoggerConfig.Default;

        Assert.That(config1, Is.Not.SameAs(config2));
    }

    [Test]
    public void プロパティを変更できる()
    {
        var config = new LoggerConfig
        {
            RecordingEnginePid = 1234,
            MaxQueueSize = 5000,
            FlushIntervalMs = 200,
            AutoWatchControls = false,
            TrackForms = false,
        };

        Assert.That(config.RecordingEnginePid, Is.EqualTo(1234));
        Assert.That(config.MaxQueueSize, Is.EqualTo(5000));
        Assert.That(config.FlushIntervalMs, Is.EqualTo(200));
        Assert.That(config.AutoWatchControls, Is.False);
        Assert.That(config.TrackForms, Is.False);
    }
}
