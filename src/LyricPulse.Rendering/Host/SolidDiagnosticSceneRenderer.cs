using LyricPulse.Core.Models;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Host;

/// <summary>
/// Minimal Phase 2 placeholder <see cref="ILyricSceneRenderer"/> that exercises the full
/// FontAssetRegistry + 4-layer LegibilityPipeline + SpringState pipeline cleanly prior to Phase 3 styles.
/// </summary>
public sealed class SolidDiagnosticSceneRenderer : ILyricSceneRenderer
{
    private SpringState _scaleSpring = new(Value: 0.92f, Velocity: 0f, Target: 1.0f);
    private string _lastHeadline = string.Empty;

    public OverlayStyleId StyleId => OverlayStyleId.Fisheye;

    public bool AdvancePhysics(in FrameRenderPacket packet)
    {
        var headline = ResolveDisplayText(packet.SceneSnapshot);
        if (!string.Equals(headline, _lastHeadline, StringComparison.Ordinal))
        {
            _lastHeadline = headline;
            _scaleSpring = packet.ReducedMotion
                ? SpringState.AtRest(1.0f)
                : new SpringState(Value: 0.94f, Velocity: 0.4f, Target: 1.0f);
        }

        if (packet.ReducedMotion)
        {
            _scaleSpring = SpringState.AtRest(1.0f);
            return false;
        }

        _scaleSpring = _scaleSpring.Step(SpringTokens.WordSnap, packet.DeltaSeconds);
        return !_scaleSpring.IsAtRest || packet.SceneSnapshot.PlaybackState == MediaPlaybackState.Playing;
    }

    public void Render(in StyleRenderContext context)
    {
        var canvas = context.Canvas;
        var packet = context.Packet;
        canvas.Clear(SKColors.Transparent);

        var text = ResolveDisplayText(packet.SceneSnapshot);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var font = context.Fonts.GetOrCreateFont(packet.ActiveFontId, packet.BaseFontSizeDip);
        var measuredWidth = TextShaperService.MeasureWidth(text, font);
        var fitScale = TextShaperService.ComputeAutoFitScale(measuredWidth, packet.ViewportWidthDip, 0.80f);
        var totalScale = fitScale * _scaleSpring.Value;

        var anchorX = (float)(packet.ViewportWidthDip * packet.BoundsConfig.AnchorX);
        var anchorY = (float)(packet.ViewportHeightDip * packet.BoundsConfig.AnchorY);

        var halfW = (measuredWidth * totalScale) * 0.5f;
        var halfH = (font.Size * totalScale) * 0.65f;
        var clusterBounds = new SKRect(anchorX - halfW, anchorY - halfH, anchorX + halfW, anchorY + halfH);

        var opacitySave = context.Legibility.BeginGlobalOpacityLayer(canvas, packet.GlobalOpacity);
        try
        {
            context.Legibility.DrawAmbientScrim(
                canvas,
                clusterBounds,
                font.Size * totalScale,
                packet.Palette.ScrimTint,
                packet.ScrimIntensity,
                packet.HighContrastScrim);

            var transformSave = canvas.Save();
            try
            {
                canvas.Translate(anchorX, anchorY);
                canvas.Scale(totalScale, totalScale);

                var baselineOffset = font.Size * 0.35f;
                context.Legibility.DrawLegibleText(
                    canvas,
                    text,
                    x: 0f,
                    y: baselineOffset,
                    SKTextAlign.Center,
                    font,
                    packet.ActiveFontId,
                    packet.Palette.AccentColor1,
                    opacity: 1.0f,
                    activeGlowColor: packet.Palette.AccentColor2);
            }
            finally
            {
                canvas.RestoreToCount(transformSave);
            }
        }
        finally
        {
            LegibilityPipeline.EndGlobalOpacityLayer(canvas, opacitySave);
        }
    }

    private static string ResolveDisplayText(LyricSceneSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.DiagnosticHeadline))
        {
            return snapshot.DiagnosticHeadline;
        }

        if (snapshot.Document is { } doc &&
            snapshot.ActiveLineIndex >= 0 &&
            snapshot.ActiveLineIndex < doc.Lines.Length)
        {
            return doc.Lines[snapshot.ActiveLineIndex].RawText;
        }

        return snapshot.Track is { IsValid: true } track
            ? $"{track.RawArtist} — {track.RawTitle}"
            : string.Empty;
    }
}
