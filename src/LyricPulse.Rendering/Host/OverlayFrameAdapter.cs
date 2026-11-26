using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Fonts;

namespace LyricPulse.Rendering.Host;

/// <summary>
/// Pure zero-side-effect bridge converting a Core <see cref="OverlayPlaybackSnapshot"/>
/// into an immutable <see cref="FrameRenderPacket"/> for the SkiaSharp render thread.
/// </summary>
public static class OverlayFrameAdapter
{
    public static FrameRenderPacket FromPlaybackSnapshot(
        OverlayPlaybackSnapshot snapshot,
        float viewportWidthDip,
        float viewportHeightDip,
        float renderScaling,
        double renderTimeSeconds,
        float deltaSeconds,
        OverlayBoundsConfig? boundsOverride = null,
        bool systemReducedMotion = false,
        DateTimeOffset? utcNow = null,
        StageRenderSurfaceMode surfaceMode = StageRenderSurfaceMode.DesktopOverlay,
        StageBackdropMode? stageBackdropOverride = null,
        OverlayStyleId? styleOverride = null,
        float? heroScaleOverride = null,
        bool bottomFadeToBlack = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var settings = snapshot.Settings;
        var safeW = Math.Max(1f, viewportWidthDip);
        var safeH = Math.Max(1f, viewportHeightDip);
        var bounds = boundsOverride ?? OverlayBoundsConfig.FromAnchorZone(settings.AnchorZone, settings.SafeMarginPx);
        var scene = new LyricSceneSnapshot(
            PlaybackState: snapshot.PlaybackState,
            EffectivePosition: snapshot.EffectivePosition,
            Track: snapshot.Track,
            Document: snapshot.Document,
            ActiveLineIndex: snapshot.ActiveLineIndex,
            ActiveWordIndex: snapshot.ActiveWordIndex,
            DiagnosticHeadline: null);

        var heroScale = heroScaleOverride is { } explicitScale && float.IsFinite(explicitScale) && explicitScale > 0f
            ? Math.Clamp(explicitScale, 0.25f, 3.0f)
            : ComputeRecommendedHeroScale(surfaceMode, settings, safeW, safeH);

        var effectiveBackdrop = ResolveBackdropMode(surfaceMode, settings, stageBackdropOverride);
        var effectivePalette = surfaceMode == StageRenderSurfaceMode.StageCanvas && settings.EdgeGlowMode == EdgeGlowMode.NeonRush
            ? snapshot.ActivePalette with { ScrimTint = new RgbColor(0, 0, 0) }
            : snapshot.ActivePalette;
        var effectiveOpacity = surfaceMode is StageRenderSurfaceMode.StageCanvas or StageRenderSurfaceMode.GalleryCard
            ? 1.0f
            : (float)Math.Clamp(settings.OverlayOpacity, AppSettings.MinOverlayOpacity, AppSettings.MaxOverlayOpacity);

        return new FrameRenderPacket(
            ActiveStyle: settings.ActiveStyle,
            RenderTimeSeconds: renderTimeSeconds,
            DeltaSeconds: deltaSeconds,
            ViewportWidthDip: safeW,
            ViewportHeightDip: safeH,
            RenderScaling: float.IsFinite(renderScaling) && renderScaling > 0f ? renderScaling : 1.0f,
            GlobalOpacity: effectiveOpacity,
            ReducedMotion: snapshot.IsEffectiveReducedMotion || systemReducedMotion,
            HighContrastScrim: settings.HighContrastScrim,
            ScrimIntensity: settings.ScrimIntensity,
            ActiveFontId: FontAssetRegistry.ResolveFontId(settings.FontFamily),
            BaseFontSizeDip: (float)settings.ComputeEffectiveFontSizeDip(),
            Palette: effectivePalette,
            BoundsConfig: bounds,
            SceneSnapshot: scene,
            PlaybackSnapshot: snapshot,
            UtcNow: utcNow ?? DateTimeOffset.UtcNow,
            SurfaceMode: surfaceMode,
            StageBackdrop: effectiveBackdrop,
            HeroScaleMultiplier: heroScale,
            PartyEnergyLevel: settings.PartyEnergyLevel,
            ShowFollowingDot: settings.ShowFollowingDot,
            ShowColorSweepLayer: settings.ShowColorSweepLayer,
            SecondaryLineMode: settings.SecondaryLineMode,
            StyleOverride: styleOverride,
            AudioMetrics: snapshot.EffectiveAudioMetrics,
            BottomFadeToBlack: bottomFadeToBlack);
    }

    public static float ComputeRecommendedHeroScale(
        StageRenderSurfaceMode surfaceMode,
        AppSettings settings,
        float viewportWidthDip,
        float viewportHeightDip)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return surfaceMode switch
        {
            StageRenderSurfaceMode.StageCanvas => Math.Clamp(
                settings.FullscreenStageScale,
                AppSettings.MinFullscreenStageScale,
                AppSettings.MaxFullscreenStageScale),
            StageRenderSurfaceMode.GalleryCard => Math.Clamp(
                Math.Min(viewportWidthDip / 520f, viewportHeightDip / 260f),
                0.36f,
                0.76f),
            _ => 1.0f
        };
    }

    private static StageBackdropMode ResolveBackdropMode(
        StageRenderSurfaceMode surfaceMode,
        AppSettings settings,
        StageBackdropMode? backdropOverride)
    {
        if (backdropOverride.HasValue)
        {
            return backdropOverride.Value;
        }

        return surfaceMode switch
        {
            StageRenderSurfaceMode.StageCanvas or StageRenderSurfaceMode.GalleryCard =>
                settings.StageBackdropMode == StageBackdropMode.TransparentOverlay
                    ? StageBackdropMode.AmbientPartyMesh
                    : settings.StageBackdropMode,
            _ => StageBackdropMode.TransparentOverlay
        };
    }
}
