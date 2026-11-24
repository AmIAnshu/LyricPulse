using Avalonia.Controls;
using Microsoft.Win32;
using SkiaSharp;

namespace LyricPulse.App.Tray;

public enum TrayIconVisualState
{
    Playing = 0,
    Idle = 1,
    PausedOrHidden = 2,
    Error = 3
}

/// <summary>
/// Programmatically generates crisp vector-rendered multi-state Avalonia <see cref="WindowIcon"/>
/// bitmaps adapted to the Windows 10/11 taskbar polarity (Dark vs Light taskbar). Zero emoji chrome.
/// </summary>
public sealed class TrayIconFactory
{
    private const string PersonalizeSubKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemUsesLightThemeValue = "SystemUsesLightTheme";

    private static readonly SKColor DarkTaskbarActive = new(0xF8, 0xFA, 0xFC, 0xFF);
    private static readonly SKColor DarkTaskbarIdle = new(0xE2, 0xE8, 0xF0, 0xFF);
    private static readonly SKColor LightTaskbarActive = new(0x11, 0x14, 0x20, 0xFF);
    private static readonly SKColor LightTaskbarIdle = new(0x1E, 0x29, 0x3B, 0xFF);
    private static readonly SKColor LiveGreenDot = new(0x22, 0xC5, 0x5E, 0xFF);
    private static readonly SKColor WarningAmberDot = new(0xF5, 0x9E, 0x0B, 0xFF);

    private readonly Dictionary<(TrayIconVisualState State, bool IsLightTaskbar), WindowIcon> _cache = new();

    public static bool IsSystemLightTaskbar()
    {
        try
        {
            var raw = Registry.GetValue(PersonalizeSubKey, SystemUsesLightThemeValue, defaultValue: 0);
            return raw is int intVal && intVal == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public WindowIcon GetIcon(TrayIconVisualState state, bool? isLightTaskbarOverride = null)
    {
        var isLight = isLightTaskbarOverride ?? IsSystemLightTaskbar();
        var key = (state, isLight);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var pngBytes = RenderIconPngBytes(state, isLight, sizePx: 32);
        using var stream = new MemoryStream(pngBytes, writable: false);
        var icon = new WindowIcon(stream);
        _cache[key] = icon;
        return icon;
    }

    public static byte[] RenderIconPngBytes(TrayIconVisualState state, bool isLightTaskbar, int sizePx = 32)
    {
        var dim = Math.Clamp(sizePx, 16, 64);
        var info = new SKImageInfo(dim, dim, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var glyphColor = ResolveWaveformColor(state, isLightTaskbar);

        // Draw crisp 3-bar kinetic LyricPulse waveform mark
        using var barPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = glyphColor
        };

        var barWidth = dim * 0.16f;
        var barGap = dim * 0.10f;
        var totalWidth = (barWidth * 3f) + (barGap * 2f);
        var startX = (dim - totalWidth) * 0.5f;
        var centerY = dim * 0.50f;
        var corner = barWidth * 0.48f;

        ReadOnlySpan<float> heights = [dim * 0.42f, dim * 0.76f, dim * 0.54f];
        for (var i = 0; i < 3; i++)
        {
            var x = startX + (i * (barWidth + barGap));
            var h = heights[i];
            var rect = new SKRect(x, centerY - (h * 0.5f), x + barWidth, centerY + (h * 0.5f));
            canvas.DrawRoundRect(rect, corner, corner, barPaint);
        }

        if (state == TrayIconVisualState.PausedOrHidden)
        {
            // Crisp diagonal slash indicating lyrics are hidden/paused
            using var slashPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeWidth = Math.Max(2.0f, dim * 0.085f),
                Color = isLightTaskbar ? LightTaskbarActive : DarkTaskbarActive
            };
            canvas.DrawLine(dim * 0.16f, dim * 0.84f, dim * 0.84f, dim * 0.16f, slashPaint);
        }
        else if (state is TrayIconVisualState.Playing or TrayIconVisualState.Error)
        {
            var dotRadius = Math.Max(3.0f, dim * 0.165f);
            var dotX = dim - dotRadius - (dim * 0.06f);
            var dotY = dim - dotRadius - (dim * 0.06f);

            // Subtle halo separator ring behind status dot for crispness on any taskbar color
            using var ringPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = isLightTaskbar ? new SKColor(0xF8, 0xFA, 0xFC, 0xE0) : new SKColor(0x0B, 0x0C, 0x16, 0xE0)
            };
            canvas.DrawCircle(dotX, dotY, dotRadius + Math.Max(1.0f, dim * 0.04f), ringPaint);

            using var dotPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = state == TrayIconVisualState.Playing ? LiveGreenDot : WarningAmberDot
            };
            canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
        }

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKColor ResolveWaveformColor(TrayIconVisualState state, bool isLightTaskbar) =>
        (state, isLightTaskbar) switch
        {
            (TrayIconVisualState.Playing, false) => DarkTaskbarActive,
            (TrayIconVisualState.Playing, true) => LightTaskbarActive,
            (TrayIconVisualState.Error, false) => DarkTaskbarActive,
            (TrayIconVisualState.Error, true) => LightTaskbarActive,
            (TrayIconVisualState.PausedOrHidden, false) => DarkTaskbarIdle.WithAlpha(166),
            (TrayIconVisualState.PausedOrHidden, true) => LightTaskbarIdle.WithAlpha(166),
            (_, false) => DarkTaskbarIdle,
            (_, true) => LightTaskbarIdle
        };
}
