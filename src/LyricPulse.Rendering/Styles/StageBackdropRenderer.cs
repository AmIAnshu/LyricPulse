using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Renders full-viewport Stage Canvas and Gallery Card backdrops (<c>AmbientPartyMesh</c> via SkSL
/// with beat-reactive energy pulses, <c>PureBlack</c> OLED concert obsidian, or static gradient fallback
/// under Reduced Motion / software rendering). Includes curated Verci-caliber style mood palettes
/// when album covers are monochrome or grayscale so stages always glow luminously.
/// </summary>
public sealed class StageBackdropRenderer : IDisposable
{
    public static readonly SKColor PureBlackStageColor = SKColors.Black; // #000000 true OLED pitch black
    public static readonly SKColor ObsidianMeshBase = new(0x09, 0x0A, 0x10, 0xFF); // #090A10

    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKColor[] _radialColors = new SKColor[3];
    private readonly float[] _radialStops = [0.0f, 0.55f, 1.0f];
    private readonly SKColor[] _fadeColors = [SKColors.Transparent, new SKColor(0, 0, 0, 255)];
    private readonly float[] _fadeStops = [0f, 1f];

    private float _meshTimeSec;
    private float _beatPulse;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private bool _disposed;

    internal bool AllowCpuShadersForTesting { get; set; }

    public static AlbumAccentPalette ResolveBackdropPalette(
        OverlayStyleId styleId,
        AlbumAccentPalette albumPalette,
        bool autoSyncColors)
    {
        ArgumentNullException.ThrowIfNull(albumPalette);

        var needsCuratedMood = albumPalette.IsMonochromeFallback ||
                               (autoSyncColors && string.IsNullOrEmpty(albumPalette.SourceAlbumHash));
        if (!needsCuratedMood)
        {
            return albumPalette;
        }

        // Rich Verci-grade dual-tone atmospheric palettes when album cover is grayscale/absent
        return styleId switch
        {
            OverlayStyleId.Fisheye => new AlbumAccentPalette(new RgbColor(0xEC, 0x48, 0x99), new RgbColor(0xFB, 0x71, 0x85), new RgbColor(0xA8, 0x55, 0xF7), new RgbColor(0x16, 0x0B, 0x1A), false, albumPalette.SourceAlbumHash),
            OverlayStyleId.FisheyeVisual => new AlbumAccentPalette(new RgbColor(0xF9, 0x73, 0x16), new RgbColor(0xEC, 0x48, 0x99), new RgbColor(0xFB, 0xBF, 0x24), new RgbColor(0x1B, 0x0D, 0x14), false, albumPalette.SourceAlbumHash),
            OverlayStyleId.Codestyle => new AlbumAccentPalette(new RgbColor(0x38, 0xBD, 0xF8), new RgbColor(0xF4, 0x3F, 0x5E), new RgbColor(0x81, 0x8C, 0xF8), new RgbColor(0x0B, 0x10, 0x1E), false, albumPalette.SourceAlbumHash),
            OverlayStyleId.CodestyleVisual => new AlbumAccentPalette(new RgbColor(0xA8, 0x55, 0xF7), new RgbColor(0xEC, 0x48, 0x99), new RgbColor(0x38, 0xBD, 0xF8), new RgbColor(0x13, 0x0B, 0x20), false, albumPalette.SourceAlbumHash),
            OverlayStyleId.VisualStack => new AlbumAccentPalette(new RgbColor(0x10, 0xB9, 0x81), new RgbColor(0xF5, 0x9E, 0x0B), new RgbColor(0x34, 0xD3, 0x99), new RgbColor(0x09, 0x16, 0x12), false, albumPalette.SourceAlbumHash),
            OverlayStyleId.Constellation => new AlbumAccentPalette(new RgbColor(0x63, 0x66, 0xF1), new RgbColor(0xC0, 0x84, 0xFC), new RgbColor(0x81, 0x8C, 0xF8), new RgbColor(0x0D, 0x0C, 0x22), false, albumPalette.SourceAlbumHash),
            OverlayStyleId.SpidersWeb => new AlbumAccentPalette(new RgbColor(0xF5, 0x9E, 0x0B), new RgbColor(0xEA, 0x58, 0x0C), new RgbColor(0xFD, 0xBA, 0x74), new RgbColor(0x1C, 0x0F, 0x08), false, albumPalette.SourceAlbumHash),
            OverlayStyleId.NodeLine => new AlbumAccentPalette(new RgbColor(0x22, 0xC5, 0x5E), new RgbColor(0x2D, 0xD4, 0xBF), new RgbColor(0x86, 0xEF, 0xAC), new RgbColor(0x08, 0x18, 0x11), false, albumPalette.SourceAlbumHash),
            OverlayStyleId.KineticWall => new AlbumAccentPalette(new RgbColor(0x0E, 0xA5, 0xE9), new RgbColor(0xFB, 0x92, 0x3C), new RgbColor(0x38, 0xBD, 0xF8), new RgbColor(0x09, 0x13, 0x1E), false, albumPalette.SourceAlbumHash),
            OverlayStyleId.Messages => new AlbumAccentPalette(new RgbColor(0x25, 0x63, 0xEB), new RgbColor(0x06, 0xB6, 0xD4), new RgbColor(0x60, 0xA5, 0xFA), new RgbColor(0x09, 0x12, 0x24), false, albumPalette.SourceAlbumHash),
            OverlayStyleId.VinylGroove => new AlbumAccentPalette(new RgbColor(0xFE, 0xE2, 0xE2), new RgbColor(0xF5, 0x9E, 0x0B), new RgbColor(0xEC, 0x48, 0x99), new RgbColor(0x16, 0x0C, 0x14), false, albumPalette.SourceAlbumHash),
            _ => albumPalette
        };
    }

    public void Reset()
    {
        _beatPulse = 0f;
        _lastWordKey = (-1, -1);
    }

    public bool Advance(in FrameRenderPacket packet)
    {
        if (!packet.ShouldDrawStageBackdrop || packet.StageBackdrop != StageBackdropMode.AmbientPartyMesh)
        {
            _beatPulse = 0f;
            return false;
        }

        if (packet.ReducedMotion)
        {
            _beatPulse = 0f;
            return false;
        }

        var dt = Math.Clamp(packet.DeltaSeconds, 0f, SpringState.MaxDeltaSeconds);
        _meshTimeSec = (_meshTimeSec + dt) % 3600f;

        var slice = StyleLayoutHelper.ResolveSlice(packet);
        var key = (slice.ActiveLineIndex, slice.ActiveWordIndex);
        if (key.ActiveLineIndex >= 0 && key != _lastWordKey)
        {
            _lastWordKey = key;
            _beatPulse = Math.Min(0.35f, _beatPulse + (0.24f * packet.SafePartyEnergy));
        }

        if (_beatPulse > 0.001f)
        {
            _beatPulse = Math.Max(0f, _beatPulse - (dt * 1.15f));
        }

        return true;
    }

    public void Render(in StyleRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        if (!packet.ShouldDrawStageBackdrop)
        {
            return;
        }

        var rect = new SKRect(0f, 0f, packet.ViewportWidthDip, packet.ViewportHeightDip);
        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            return;
        }

        var autoSync = packet.PlaybackSnapshot?.Settings.AutoSyncColors ?? true;
        var resolvedPalette = ResolveBackdropPalette(packet.EffectiveStyle, packet.Palette, autoSync);

        if (packet.StageBackdrop == StageBackdropMode.PureBlack)
        {
            DrawPureBlackBackdrop(context.Canvas, rect, resolvedPalette);
            return;
        }

        var audio = packet.EffectiveAudioMetrics;
        var bass = Math.Clamp(audio.BassEnergy + (audio.BeatPulse * 0.45f), 0f, 1f);
        var totalEnergy = Math.Clamp(packet.SafePartyEnergy + _beatPulse + (bass * 0.28f), 0.5f, 2.0f);

        var renderedShader = context.Shaders.DrawAmbientPartyMesh(
            context.Canvas,
            rect,
            packet.RenderScaling,
            _meshTimeSec,
            totalEnergy,
            resolvedPalette,
            isGpuBackend: context.IsGpuBackend,
            reducedMotion: packet.ReducedMotion,
            allowCpuForTesting: AllowCpuShadersForTesting,
            bassEnergy: bass,
            bottomFadeToBlack: packet.BottomFadeToBlack);

        if (!renderedShader)
        {
            DrawStaticGradientFallback(context.Canvas, rect, resolvedPalette, packet.SafePartyEnergy, packet.BottomFadeToBlack);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fillPaint.Dispose();
    }

    private void DrawPureBlackBackdrop(SKCanvas canvas, SKRect rect, AlbumAccentPalette palette)
    {
        _ = palette;
        _fillPaint.Shader = null;
        _fillPaint.Color = PureBlackStageColor;
        canvas.DrawRect(rect, _fillPaint);
    }

    private void DrawStaticGradientFallback(
        SKCanvas canvas,
        SKRect rect,
        AlbumAccentPalette palette,
        float partyEnergy,
        bool bottomFadeToBlack)
    {
        _fillPaint.Shader = null;
        _fillPaint.Color = ObsidianMeshBase;
        canvas.DrawRect(rect, _fillPaint);

        var boost = Math.Clamp(0.28f + (0.14f * partyEnergy), 0.24f, 0.50f);
        DrawSoftOrb(canvas, rect, new SKPoint(rect.Width * 0.22f, rect.Height * 0.30f), rect.Width * 0.65f, palette.AccentColor1, boost);
        DrawSoftOrb(canvas, rect, new SKPoint(rect.Width * 0.78f, rect.Height * 0.66f), rect.Width * 0.65f, palette.AccentColor2, boost * 0.94f);
        DrawSoftOrb(canvas, rect, new SKPoint(rect.Width * 0.52f, rect.Height * 0.45f), rect.Width * 0.52f, palette.AccentColor3, boost * 0.58f);

        if (bottomFadeToBlack)
        {
            var fadeTop = rect.Height * 0.72f;
            using var fade = SKShader.CreateLinearGradient(
                new SKPoint(rect.MidX, fadeTop),
                new SKPoint(rect.MidX, rect.Bottom),
                _fadeColors,
                _fadeStops,
                SKShaderTileMode.Clamp);
            _fillPaint.Shader = fade;
            _fillPaint.Color = SKColors.White;
            try
            {
                canvas.DrawRect(new SKRect(rect.Left, fadeTop, rect.Right, rect.Bottom), _fillPaint);
            }
            finally
            {
                _fillPaint.Shader = null;
            }
        }
    }

    private void DrawSoftOrb(
        SKCanvas canvas,
        SKRect bounds,
        SKPoint center,
        float radius,
        RgbColor color,
        float peakAlpha)
    {
        _radialColors[0] = color.ToSkColorWithAlpha(peakAlpha);
        _radialColors[1] = color.ToSkColorWithAlpha(peakAlpha * 0.35f);
        _radialColors[2] = color.ToSkColorWithAlpha(0f);

        using var shader = SKShader.CreateRadialGradient(
            center,
            Math.Max(radius, 1f),
            _radialColors,
            _radialStops,
            SKShaderTileMode.Clamp);

        _fillPaint.Shader = shader;
        _fillPaint.Color = SKColors.White;
        try
        {
            canvas.DrawRect(bounds, _fillPaint);
        }
        finally
        {
            _fillPaint.Shader = null;
        }
    }
}
