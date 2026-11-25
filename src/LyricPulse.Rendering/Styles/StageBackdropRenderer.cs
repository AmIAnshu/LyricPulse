using LyricPulse.Core.Models;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Renders full-viewport Stage Canvas and Gallery Card backdrops (<c>AmbientPartyMesh</c> via SkSL
/// with beat-reactive energy pulses, <c>PureBlack</c> OLED concert obsidian, or static gradient fallback
/// under Reduced Motion / software rendering). Automatically skips on <see cref="StageRenderSurfaceMode.DesktopOverlay"/>
/// so click-through desktop overlays stay 100% transparent.
/// </summary>
public sealed class StageBackdropRenderer : IDisposable
{
    public static readonly SKColor PureBlackStageColor = new(0x05, 0x05, 0x08, 0xFF); // #050508
    public static readonly SKColor ObsidianMeshBase = new(0x08, 0x09, 0x0D, 0xFF);    // #08090D

    private readonly SKPaint _fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKColor[] _radialColors = new SKColor[3];
    private readonly float[] _radialStops = [0.0f, 0.55f, 1.0f];

    private float _meshTimeSec;
    private float _beatPulse;
    private (int LineIndex, int WordIndex) _lastWordKey = (-1, -1);
    private bool _disposed;

    internal bool AllowCpuShadersForTesting { get; set; }

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

        if (packet.StageBackdrop == StageBackdropMode.PureBlack)
        {
            DrawPureBlackBackdrop(context.Canvas, rect, packet.Palette);
            return;
        }

        var totalEnergy = Math.Clamp(packet.SafePartyEnergy + _beatPulse, 0.5f, 1.85f);
        var renderedShader = context.Shaders.DrawAmbientPartyMesh(
            context.Canvas,
            rect,
            packet.RenderScaling,
            _meshTimeSec,
            totalEnergy,
            packet.Palette,
            isGpuBackend: context.IsGpuBackend,
            reducedMotion: packet.ReducedMotion,
            allowCpuForTesting: AllowCpuShadersForTesting);

        if (!renderedShader)
        {
            DrawStaticGradientFallback(context.Canvas, rect, packet.Palette, packet.SafePartyEnergy);
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
        _fillPaint.Shader = null;
        _fillPaint.Color = PureBlackStageColor;
        canvas.DrawRect(rect, _fillPaint);

        // Ultra-subtle ambient stage vignette marrying lyrics to album mood without lifting pure black corners
        var radius = MathF.Max(rect.Width, rect.Height) * 0.55f;
        _radialColors[0] = palette.ScrimTint.ToSkColorWithAlpha(0.28f);
        _radialColors[1] = palette.ScrimTint.ToSkColorWithAlpha(0.08f);
        _radialColors[2] = SKColors.Transparent;

        using var vignette = SKShader.CreateRadialGradient(
            new SKPoint(rect.MidX, rect.MidY),
            Math.Max(radius, 1f),
            _radialColors,
            _radialStops,
            SKShaderTileMode.Clamp);

        _fillPaint.Shader = vignette;
        _fillPaint.Color = SKColors.White;
        try
        {
            canvas.DrawRect(rect, _fillPaint);
        }
        finally
        {
            _fillPaint.Shader = null;
        }
    }

    private void DrawStaticGradientFallback(
        SKCanvas canvas,
        SKRect rect,
        AlbumAccentPalette palette,
        float partyEnergy)
    {
        _fillPaint.Shader = null;
        _fillPaint.Color = ObsidianMeshBase;
        canvas.DrawRect(rect, _fillPaint);

        var boost = Math.Clamp(0.22f + (0.12f * partyEnergy), 0.20f, 0.42f);
        DrawSoftOrb(canvas, rect, new SKPoint(rect.Width * 0.24f, rect.Height * 0.30f), rect.Width * 0.58f, palette.AccentColor1, boost);
        DrawSoftOrb(canvas, rect, new SKPoint(rect.Width * 0.78f, rect.Height * 0.68f), rect.Width * 0.60f, palette.AccentColor2, boost * 0.92f);
        DrawSoftOrb(canvas, rect, new SKPoint(rect.Width * 0.52f, rect.Height * 0.50f), rect.Width * 0.50f, palette.AccentColor3, boost * 0.55f);
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
