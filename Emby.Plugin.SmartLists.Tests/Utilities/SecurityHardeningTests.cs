using System.Diagnostics;
using System.Text.RegularExpressions;
using Emby.Plugin.SmartLists.Core.QueryEngine;
using Emby.Plugin.SmartLists.Core.QueryEngine.Prefilters;
using Emby.Plugin.SmartLists.Utilities;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Emby.Plugin.SmartLists.Tests;

/// <summary>Pins the fixes from the security review: regex timeouts and the limits on decoding cover pictures.</summary>
public sealed class SecurityHardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smartlists-sec-" + Guid.NewGuid().ToString("N"));

    public SecurityHardeningTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp folder.
        }
    }

    // ----- regex -----

    [Fact]
    public void CachedRegexesAlwaysHaveTheMatchTimeout()
    {
        var regex = Engine.GetOrCreateRegex("^Bulk.*[0-9]+$");
        Assert.Equal(Engine.RegexMatchTimeout, regex.MatchTimeout);
    }

    [Fact]
    public void CatastrophicPatternFailsLoudlyInsteadOfPinningACore()
    {
        var regex = Engine.GetOrCreateRegex("^(a+)+$");
        var subject = new string('a', 45) + "!";

        var watch = Stopwatch.StartNew();
        Assert.Throws<ArgumentException>(() => Engine.RegexIsMatch(regex, subject));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "the match timeout must stop the evaluation");
    }

    [Fact]
    public void MoreDistinctPatternsThanTheCacheHoldsStillWork()
    {
        for (var i = 0; i < 1200; i++)
        {
            Assert.True(Engine.RegexIsMatch(Engine.GetOrCreateRegex("^pattern" + i + "$"), "pattern" + i));
        }
    }

    [Fact]
    public void PrefilterGateSurvivesAPatternThatIsSlowEvenOnTheEmptyString()
    {
        Assert.True(PrefilterStringMatcher.RegexMatchesEmptyOrIsInvalid("("));
        Assert.True(PrefilterStringMatcher.RegexMatchesEmptyOrIsInvalid("^$"));
        Assert.False(PrefilterStringMatcher.RegexMatchesEmptyOrIsInvalid("^abc$"));
    }

    // ----- cover pictures -----

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception) + " " + exception?.Message);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        var length = BitConverter.GetBytes(data.Length).Reverse().ToArray();
        var crc = BitConverter.GetBytes(Crc32(body)).Reverse().ToArray();
        return length.Concat(body).Concat(crc).ToArray();
    }

    // A PNG file of a few dozen bytes whose header claims the given size: the kind of file that decodes into gigabytes.
    private string WritePixelBomb(int width, int height)
    {
        var ihdr = BitConverter.GetBytes(width).Reverse().Concat(BitConverter.GetBytes(height).Reverse()).Concat(new byte[] { 8, 6, 0, 0, 0 }).ToArray();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat(Chunk("IHDR", ihdr))
            .Concat(Chunk("IDAT", new byte[] { 0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 }))
            .Concat(Chunk("IEND", Array.Empty<byte>()))
            .ToArray();
        var path = Path.Combine(_dir, $"bomb-{width}x{height}.png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public async Task OrdinaryPictureStillMakesACover()
    {
        var source = Path.Combine(_dir, "poster.png");
        using (var image = new Image<Rgba32>(300, 450))
        {
            image.SaveAsPng(source);
        }

        var output = Path.Combine(_dir, "cover.jpg");
        var ok = await CollageBuilder.TryCreateCroppedCoverAsync(source, output, 1, 1, 0, false, new CapturingLogger(), CancellationToken.None);

        Assert.True(ok);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public async Task PictureThatClaimsAbsurdDimensionsIsRefusedBeforeDecoding()
    {
        var source = WritePixelBomb(60_000, 60_000);
        var output = Path.Combine(_dir, "bomb-cover.jpg");
        var logger = new CapturingLogger();

        var watch = Stopwatch.StartNew();
        var ok = await CollageBuilder.TryCreateCroppedCoverAsync(source, output, 1, 1, 0, false, logger, CancellationToken.None);

        Assert.False(ok);
        Assert.False(File.Exists(output));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Contains(logger.Messages, m => m.Contains("too many pixels", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FileOverTheSizeLimitIsRefused()
    {
        var source = Path.Combine(_dir, "huge.png");
        using (var stream = File.Create(source))
        {
            stream.SetLength(51L * 1024 * 1024);
        }

        var logger = new CapturingLogger();
        var ok = await CollageBuilder.TryCreateCroppedCoverAsync(source, Path.Combine(_dir, "huge-cover.jpg"), 1, 1, 0, false, logger, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains(logger.Messages, m => m.Contains("too large", StringComparison.OrdinalIgnoreCase));
    }
}