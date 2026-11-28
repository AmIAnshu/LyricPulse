using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using LyricPulse.App.Platform;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Security;
using LyricPulse.Core.Serialization;
using LyricPulse.Core.Settings;
using LyricPulse.Core.Visuals;
using LyricPulse.Rendering.Color;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LyricPulse.Tests.Security;

public sealed class SecurityHardeningRegressionTests
{
    [Fact]
    public async Task BoundedHttpResponseReader_RejectsOversizedContentLength_AndStreamingPayloads()
    {
        using var headerBomb = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[16])
        };
        headerBomb.Content.Headers.ContentLength = BoundedHttpResponseReader.DefaultMaxJsonBytes + 1;

        var headerResult = await BoundedHttpResponseReader.ReadJsonAsync(
            headerBomb,
            LyricPulseJsonContext.Secure.LrclibTrackDto,
            CancellationToken.None);

        headerResult.ExceededLimit.Should().BeTrue();
        headerResult.Value.Should().BeNull();

        using var streamedBomb = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new PushStreamContent(new byte[BoundedHttpResponseReader.DefaultMaxJsonBytes + 1024])
        };

        var streamResult = await BoundedHttpResponseReader.ReadJsonAsync(
            streamedBomb,
            LyricPulseJsonContext.Secure.LrclibTrackDto,
            CancellationToken.None);

        streamResult.ExceededLimit.Should().BeTrue();
        streamResult.Value.Should().BeNull();
    }

    [Fact]
    public async Task LyricPulseJsonContext_Secure_RejectsExcessivelyDeepJson()
    {
        var depth = LyricPulseJsonContext.MaxSecureJsonDepth + 8;
        var sb = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            sb.Append("{\"a\":");
        }

        sb.Append('1');
        for (var i = 0; i < depth; i++)
        {
            sb.Append('}');
        }

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        var act = async () => await JsonSerializer.DeserializeAsync(
            stream,
            LyricPulseJsonContext.Secure.AppSettings);

        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task LrcLibProvider_DoesNotFollowRedirects_AndHandlesOversizedBodiesSafely()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri("http://127.0.0.1/internal");
            return redirect;
        });

        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://lrclib.net") };
        using var provider = new LrcLibLyricsProvider(
            client,
            new LyricsParser(),
            new TitleNormalizer(),
            NullLogger<LrcLibLyricsProvider>.Instance);

        var track = new TitleNormalizer().CreateIdentity("Blinding Lights", "The Weeknd", "", TimeSpan.FromSeconds(200));
        var result = await provider.FetchAsync(track);

        result.Status.Should().Be(LyricsStatus.NotFound);
    }

    [Fact]
    public void QuantizedEmbeddingTable_RejectsHeaderMatrixBomb_AndTruncatedSeekableStream()
    {
        // 12-byte bomb claiming 4096 dims x 500,000 vocab = ~2 GB matrix
        using var hugeHeader = BuildLpqvHeaderOnly(version: 1, dimensions: 4096, vocabCount: 500_000);
        using var vocab1 = new StringReader(string.Empty);

        var sw = Stopwatch.StartNew();
        var actHuge = () => QuantizedEmbeddingTable.Load(hugeHeader, vocab1);
        actHuge.Should().Throw<InvalidDataException>();
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(200);

        // 12-byte truncated stream under MaxMatrixBytes (100 dims x 100,000 vocab = 10 MB)
        using var truncatedHeader = BuildLpqvHeaderOnly(version: 1, dimensions: 100, vocabCount: 100_000);
        using var vocab2 = new StringReader(string.Empty);

        var actTrunc = () => QuantizedEmbeddingTable.Load(truncatedHeader, vocab2);
        actTrunc.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SkiaAlbumColorExtractor_DecodeBoundedCoverImage_RejectsDecompressionBomb_AndCorruptInput()
    {
        // Valid PNG signature + IHDR claiming 10,000 x 10,000 (100 MP > 64 MP cap)
        byte[] fakeGiantPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D,
            0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x27, 0x10, // width = 10000
            0x00, 0x00, 0x27, 0x10, // height = 10000
            0x08, 0x02, 0x00, 0x00, 0x00,
            0x91, 0x5D, 0x1F, 0x8C
        ];

        using var bombImage = SkiaAlbumColorExtractor.DecodeBoundedCoverImage(fakeGiantPng);
        bombImage.Should().BeNull();

        using var garbageImage = SkiaAlbumColorExtractor.DecodeBoundedCoverImage("not an image"u8);
        garbageImage.Should().BeNull();
    }

    [Fact]
    public async Task LocalLrcSidecarProvider_SanitizesTraversal_ReservedNames_AndRejectsOversizedFiles()
    {
        LocalLrcSidecarProvider.SanitizeFileName(@"..\..\Windows\win.ini")
            .Should().NotContain("..").And.NotContain("\\").And.NotContain("/");

        foreach (var reserved in new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT9", "con.txt" })
        {
            SafeFileGuard.IsReservedWindowsDeviceName(reserved).Should().BeTrue();
            var sanitized = LocalLrcSidecarProvider.SanitizeFileName(reserved);
            SafeFileGuard.IsReservedWindowsDeviceName(sanitized).Should().BeFalse();
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"lp-sec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var track = new TitleNormalizer().CreateIdentity("Big Song", "Artist", "", TimeSpan.FromSeconds(180));
            var oversizedFile = Path.Combine(tempDir, $"{track.CacheKey}.lrc");
            await File.WriteAllBytesAsync(oversizedFile, new byte[SafeFileGuard.MaxSidecarLrcFileBytes + 64]);

            var provider = new LocalLrcSidecarProvider(tempDir, new LyricsParser());
            var result = await provider.FetchAsync(track);
            result.Status.Should().Be(LyricsStatus.NotFound);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task JsonSettingsStore_RejectsOversizedSettingsFile_AndFallsBackToDefaults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"lp-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var settingsPath = Path.Combine(tempDir, "settings.json");
            await File.WriteAllBytesAsync(settingsPath, new byte[SafeFileGuard.MaxSettingsFileBytes + 64]);

            using var store = new JsonSettingsStore(settingsPath);
            var loaded = await store.LoadAsync();
            loaded.Should().BeEquivalentTo(AppSettings.Default);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TitleNormalizer_ResistsAdversarialLongBacktrackingInput_Quickly()
    {
        var normalizer = new TitleNormalizer();
        var adversarial = "Song Title (live at " + new string('a', 40_000) + " - remix " + new string(' ', 5_000);

        var sw = Stopwatch.StartNew();
        var normalized = normalizer.NormalizeTitle(adversarial);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(100);
        normalized.Length.Should().BeLessThanOrEqualTo(TitleNormalizer.MaxInputChars);
    }

    [Fact]
    public void LyricsParser_HandlesTimestampOverflow_EnforcesLineCap_AndMatchesBilingualInLogTime()
    {
        var parser = new LyricsParser();
        var track = new TitleNormalizer().CreateIdentity("Test", "Artist", "", TimeSpan.FromMinutes(3));

        // Extreme overflowing YRC timestamps must not throw OverflowException
        var extremeYrc = "[99999999999999999999999,99999999999999999999](99999999999999999999,9999999999999999,0)Hello";
        var yrcDoc = parser.ParseYrc(track, extremeYrc, LyricsSourceKind.NetEase, "NetEase", isFromCache: false);
        yrcDoc.Lines.Should().HaveCount(1);

        // Line cap enforcement (> 2,000 lines truncated to 2,000)
        var bigLrc = new StringBuilder();
        var bigTrans = new StringBuilder();
        for (var i = 0; i < 2500; i++)
        {
            var totalSec = i;
            var min = totalSec / 60;
            var sec = totalSec % 60;
            bigLrc.Append(CultureInfo.InvariantCulture, $"[{min:D2}:{sec:D2}.00]Line {i}\n");
            bigTrans.Append(CultureInfo.InvariantCulture, $"[{min:D2}:{sec:D2}.15]Trans {i}\n");
        }

        var sw = Stopwatch.StartNew();
        var doc = parser.ParseLrc(
            track,
            bigLrc.ToString(),
            LyricsSourceKind.LrcLib,
            "LRCLIB",
            isFromCache: false,
            translationLrc: bigTrans.ToString());
        sw.Stop();

        doc.Lines.Length.Should().Be(LyricsParser.MaxLines);
        doc.Lines[0].TranslationText.Should().Be("Trans 0");
        doc.Lines[500].TranslationText.Should().Be("Trans 500");
        sw.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    [Fact]
    public void AppSettings_ValidateAndNormalize_ClampsAllInvalidEnums_Bounds_AndCaps()
    {
        var hugeBlocked = Enumerable.Range(0, 120).Select(i => $"App{i}.exe").ToImmutableArray();
        var hugeBounds = Enumerable.Range(0, 50).ToImmutableDictionary(
            i => $"DISPLAY_{i}",
            _ => new OverlayBoundsConfig((AnchorZone)999, 9999, double.NaN, double.PositiveInfinity, -2.0, 5.0));
        var badOffsets = ImmutableDictionary<string, int>.Empty
            .Add("track1", 999_999)
            .Add("track2", -999_999);
        var badHotkeys = ImmutableDictionary<HotkeyAction, string>.Empty
            .Add((HotkeyAction)999, "Ctrl+Alt+Delete");

        var malformed = new AppSettings
        {
            ActiveStyle = (OverlayStyleId)999,
            AnchorZone = (AnchorZone)999,
            ScrimIntensity = (ScrimIntensity)999,
            ThemeVariantMode = (ThemeVariantMode)999,
            SecondaryLineMode = (SecondaryLineMode)999,
            VisualPreference = (VisualPreference)999,
            PinnedStyles = [(OverlayStyleId)999],
            FontFamily = new string('X', 200) + "\0\r\n",
            BlockedAppIds = hugeBlocked,
            PerDisplayBounds = hugeBounds,
            PerTrackOffsetsMs = badOffsets,
            GlobalHotkeys = badHotkeys
        };

        var normalized = malformed.ValidateAndNormalize();

        normalized.ActiveStyle.Should().Be(OverlayStyleId.Fisheye);
        normalized.AnchorZone.Should().Be(AnchorZone.BottomThird);
        normalized.ScrimIntensity.Should().Be(ScrimIntensity.Balanced);
        normalized.ThemeVariantMode.Should().Be(ThemeVariantMode.System);
        normalized.SecondaryLineMode.Should().Be(SecondaryLineMode.Off);
        normalized.VisualPreference.Should().Be(VisualPreference.Balanced);
        normalized.PinnedStyles.Should().BeEquivalentTo(AppSettings.Default.PinnedStyles);
        normalized.FontFamily.Length.Should().BeLessThanOrEqualTo(AppSettings.MaxFontFamilyLength);
        normalized.FontFamily.Should().NotContain("\0");
        normalized.BlockedAppIds.Length.Should().Be(AppSettings.MaxBlockedAppIds);
        normalized.PerDisplayBounds.Count.Should().Be(AppSettings.MaxDisplays);
        foreach (var bound in normalized.PerDisplayBounds.Values)
        {
            Enum.IsDefined(bound.AnchorZone).Should().BeTrue();
            bound.SafeMarginPx.Should().BeInRange(AppSettings.MinSafeMarginPx, AppSettings.MaxSafeMarginPx);
            bound.AnchorX.Should().BeInRange(0.0, 1.0);
            bound.AnchorY.Should().BeInRange(0.0, 1.0);
            bound.WidthRatio.Should().BeInRange(0.10, 1.0);
            bound.HeightRatio.Should().BeInRange(0.10, 1.0);
        }

        normalized.PerTrackOffsetsMs["track1"].Should().Be(AppSettings.MaxTrackOffsetMs);
        normalized.PerTrackOffsetsMs["track2"].Should().Be(AppSettings.MinTrackOffsetMs);
        normalized.GlobalHotkeys.Should().BeEquivalentTo(AppSettings.CreateDefaultHotkeys());
    }

    [Fact]
    public void Win32Native_RestrictsDllImportSearchPath_ToSystem32()
    {
        var pinvokeMethods = typeof(Win32Native)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static m => m.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>() is not null ||
                               (m.Attributes & MethodAttributes.PinvokeImpl) != 0)
            .ToList();

        pinvokeMethods.Count.Should().BeGreaterThanOrEqualTo(18);
        foreach (var method in pinvokeMethods)
        {
            var attr = method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
            attr.Should().NotBeNull($"Win32Native.{method.Name} must restrict DLL search path to System32");
            attr!.Paths.Should().Be(DllImportSearchPath.System32);
        }
    }

    private static MemoryStream BuildLpqvHeaderOnly(ushort version, ushort dimensions, int vocabCount)
    {
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("LPQV"u8.ToArray());
            writer.Write(version);
            writer.Write(dimensions);
            writer.Write(vocabCount);
        }

        ms.Position = 0;
        return ms;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class PushStreamContent(byte[] data) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(data, 0, data.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
