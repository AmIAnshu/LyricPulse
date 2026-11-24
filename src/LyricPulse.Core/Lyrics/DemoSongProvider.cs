using System.Reflection;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Loads the embedded copyright-free 12-line, 36.0s Enhanced LRC demo track ("Neon Horizon")
/// along with its curated palette and Gallery card preview phase offsets.
/// </summary>
public sealed class DemoSongProvider
{
    public const string ResourceName = "LyricPulse.Core.Assets.Demo.neon-horizon.lrc";
    public static readonly TimeSpan LoopDuration = TimeSpan.FromSeconds(36.0);
    public static AlbumAccentPalette DemoPalette => AlbumAccentPalette.DemoNeonHorizon;

    private readonly ILyricsParser _parser;
    private readonly ITitleNormalizer _normalizer;
    private readonly Lazy<LyricsDocument> _cachedDocument;

    public DemoSongProvider(ILyricsParser parser, ITitleNormalizer normalizer)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _cachedDocument = new Lazy<LyricsDocument>(LoadDocument);
    }

    public LyricsDocument GetDemoDocument() => _cachedDocument.Value;

    public static TimeSpan GetGalleryPreviewPhaseOffset(OverlayStyleId styleId) => styleId switch
    {
        OverlayStyleId.Fisheye => TimeSpan.FromSeconds(0.2),
        OverlayStyleId.FisheyeVisual => TimeSpan.FromSeconds(3.0),
        OverlayStyleId.Codestyle => TimeSpan.FromSeconds(14.4),
        OverlayStyleId.CodestyleVisual => TimeSpan.FromSeconds(17.3),
        OverlayStyleId.VisualStack => TimeSpan.FromSeconds(5.9),
        OverlayStyleId.Constellation => TimeSpan.FromSeconds(8.8),
        OverlayStyleId.SpidersWeb => TimeSpan.FromSeconds(11.7),
        OverlayStyleId.NodeLine => TimeSpan.FromSeconds(20.1),
        OverlayStyleId.KineticWall => TimeSpan.FromSeconds(28.6),
        OverlayStyleId.Messages => TimeSpan.FromSeconds(22.9),
        _ => TimeSpan.FromSeconds(0.2)
    };

    private LyricsDocument LoadDocument()
    {
        var assembly = typeof(DemoSongProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded demo lyric resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var rawLrc = reader.ReadToEnd();

        var track = _normalizer.CreateIdentity("Neon Horizon", "LyricPulse Studio", "Demo Preview", LoopDuration);
        return _parser.ParseLrc(track, rawLrc, LyricsSourceKind.Demo, "Demo", isFromCache: false);
    }
}
