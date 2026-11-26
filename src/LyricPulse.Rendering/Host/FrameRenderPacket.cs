using LyricPulse.Core.Audio;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Shaders;
using SkiaSharp;

namespace LyricPulse.Rendering.Host;

/// <summary>
/// Immutable lyric playback snapshot captured on the UI thread for safe read-only access
/// on the Avalonia/Skia compositor render thread.
/// </summary>
public sealed record LyricSceneSnapshot(
    MediaPlaybackState PlaybackState,
    TimeSpan EffectivePosition,
    TrackIdentity? Track,
    LyricsDocument? Document,
    int ActiveLineIndex,
    int ActiveWordIndex,
    string? DiagnosticHeadline = null)
{
    public static LyricSceneSnapshot Empty { get; } = new(
        MediaPlaybackState.Stopped,
        TimeSpan.Zero,
        Track: null,
        Document: null,
        ActiveLineIndex: -1,
        ActiveWordIndex: -1,
        DiagnosticHeadline: null);
}

/// <summary>
/// Rendering host surface profile controlling backdrop compositing, Hero stage scaling,
/// and anchor geometry without mutating global <see cref="AppSettings"/>.
/// </summary>
public enum StageRenderSurfaceMode
{
    DesktopOverlay = 0,
    StageCanvas = 1,
    GalleryCard = 2,
    ObsBroadcast = 3
}

/// <summary>
/// Immutable per-frame packet passed into <see cref="LyricDrawOperation"/> so the render thread
/// never reads mutable ViewModel or control fields.
/// </summary>
public sealed record FrameRenderPacket(
    OverlayStyleId ActiveStyle,
    double RenderTimeSeconds,
    float DeltaSeconds,
    float ViewportWidthDip,
    float ViewportHeightDip,
    float RenderScaling,
    float GlobalOpacity,
    bool ReducedMotion,
    bool HighContrastScrim,
    ScrimIntensity ScrimIntensity,
    LyricFontId ActiveFontId,
    float BaseFontSizeDip,
    AlbumAccentPalette Palette,
    OverlayBoundsConfig BoundsConfig,
    LyricSceneSnapshot SceneSnapshot,
    OverlayPlaybackSnapshot? PlaybackSnapshot = null,
    DateTimeOffset? UtcNow = null,
    StageRenderSurfaceMode SurfaceMode = StageRenderSurfaceMode.DesktopOverlay,
    StageBackdropMode StageBackdrop = StageBackdropMode.TransparentOverlay,
    float HeroScaleMultiplier = 1.0f,
    float PartyEnergyLevel = AppSettings.DefaultPartyEnergyLevel,
    bool ShowFollowingDot = true,
    bool ShowColorSweepLayer = true,
    SecondaryLineMode SecondaryLineMode = SecondaryLineMode.Off,
    OverlayStyleId? StyleOverride = null,
    AudioFrameMetrics? AudioMetrics = null,
    bool BottomFadeToBlack = false,
    bool AllowDesktopEdgeGlow = false,
    bool RenderDesktopLyrics = true)
{
    public AudioFrameMetrics EffectiveAudioMetrics => AudioMetrics ?? AudioFrameMetrics.Silent;
    public OverlayStyleId EffectiveStyle => StyleOverride ?? ActiveStyle;

    public bool IsCompactCard => SurfaceMode == StageRenderSurfaceMode.GalleryCard;

    public bool ShouldDrawStageBackdrop =>
        SurfaceMode is StageRenderSurfaceMode.StageCanvas or StageRenderSurfaceMode.GalleryCard &&
        StageBackdrop != StageBackdropMode.TransparentOverlay;

    public float SafeHeroScale =>
        float.IsFinite(HeroScaleMultiplier) && HeroScaleMultiplier > 0f
            ? Math.Clamp(HeroScaleMultiplier, 0.25f, 3.0f)
            : 1.0f;

    public float SafePartyEnergy =>
        float.IsFinite(PartyEnergyLevel)
            ? Math.Clamp(PartyEnergyLevel, AppSettings.MinPartyEnergyLevel, AppSettings.MaxPartyEnergyLevel)
            : AppSettings.DefaultPartyEnergyLevel;

    public float EffectiveHeroFontSizeDip =>
        Math.Clamp(BaseFontSizeDip * SafeHeroScale, 10f, 220f);

    public static FrameRenderPacket CreateDefault(
        float viewportWidthDip,
        float viewportHeightDip,
        float renderScaling = 1.0f,
        double renderTimeSeconds = 0.0,
        float deltaSeconds = 1f / 60f)
    {
        var defaults = AppSettings.Default;
        return new FrameRenderPacket(
            ActiveStyle: defaults.ActiveStyle,
            RenderTimeSeconds: renderTimeSeconds,
            DeltaSeconds: deltaSeconds,
            ViewportWidthDip: viewportWidthDip,
            ViewportHeightDip: viewportHeightDip,
            RenderScaling: renderScaling,
            GlobalOpacity: (float)defaults.OverlayOpacity,
            ReducedMotion: defaults.ReducedMotion,
            HighContrastScrim: defaults.HighContrastScrim,
            ScrimIntensity: defaults.ScrimIntensity,
            ActiveFontId: FontAssetRegistry.ResolveFontId(defaults.FontFamily),
            BaseFontSizeDip: (float)defaults.ComputeEffectiveFontSizeDip(),
            Palette: AlbumAccentPalette.Default,
            BoundsConfig: OverlayBoundsConfig.FromAnchorZone(defaults.AnchorZone, defaults.SafeMarginPx),
            SceneSnapshot: LyricSceneSnapshot.Empty,
            PlaybackSnapshot: null,
            UtcNow: DateTimeOffset.UtcNow,
            SurfaceMode: StageRenderSurfaceMode.DesktopOverlay,
            StageBackdrop: StageBackdropMode.TransparentOverlay,
            HeroScaleMultiplier: 1.0f,
            PartyEnergyLevel: defaults.PartyEnergyLevel,
            ShowFollowingDot: defaults.ShowFollowingDot,
            ShowColorSweepLayer: defaults.ShowColorSweepLayer,
            SecondaryLineMode: defaults.SecondaryLineMode,
            StyleOverride: null);
    }
}

/// <summary>
/// Render-thread context passed to <see cref="ILyricSceneRenderer.Render"/> during an active Skia lease.
/// </summary>
public readonly record struct StyleRenderContext(
    SKCanvas Canvas,
    GRContext? GrContext,
    FontAssetRegistry Fonts,
    ShaderLibrary Shaders,
    OffscreenSurfacePool SurfacePool,
    LegibilityPipeline Legibility,
    FrameRenderPacket Packet,
    bool IsGpuBackend,
    bool ShadersEnabled);
