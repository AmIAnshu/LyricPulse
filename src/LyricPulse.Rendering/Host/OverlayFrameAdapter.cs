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
        DateTimeOffset? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var settings = snapshot.Settings;
        var bounds = boundsOverride ?? OverlayBoundsConfig.FromAnchorZone(settings.AnchorZone, settings.SafeMarginPx);
        var scene = new LyricSceneSnapshot(
            PlaybackState: snapshot.PlaybackState,
            EffectivePosition: snapshot.EffectivePosition,
            Track: snapshot.Track,
            Document: snapshot.Document,
            ActiveLineIndex: snapshot.ActiveLineIndex,
            ActiveWordIndex: snapshot.ActiveWordIndex,
            DiagnosticHeadline: null);

        return new FrameRenderPacket(
            ActiveStyle: settings.ActiveStyle,
            RenderTimeSeconds: renderTimeSeconds,
            DeltaSeconds: deltaSeconds,
            ViewportWidthDip: Math.Max(1f, viewportWidthDip),
            ViewportHeightDip: Math.Max(1f, viewportHeightDip),
            RenderScaling: float.IsFinite(renderScaling) && renderScaling > 0f ? renderScaling : 1.0f,
            GlobalOpacity: (float)Math.Clamp(settings.OverlayOpacity, AppSettings.MinOverlayOpacity, AppSettings.MaxOverlayOpacity),
            ReducedMotion: snapshot.IsEffectiveReducedMotion || systemReducedMotion,
            HighContrastScrim: settings.HighContrastScrim,
            ScrimIntensity: settings.ScrimIntensity,
            ActiveFontId: FontAssetRegistry.ResolveFontId(settings.FontFamily),
            BaseFontSizeDip: (float)settings.ComputeEffectiveFontSizeDip(),
            Palette: snapshot.ActivePalette,
            BoundsConfig: bounds,
            SceneSnapshot: scene,
            PlaybackSnapshot: snapshot,
            UtcNow: utcNow ?? DateTimeOffset.UtcNow);
    }
}
