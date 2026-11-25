using System.Globalization;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Immutable per-frame visibility, crossfade, instrumental indicator, and offset toast state
/// computed by <see cref="PlaybackLifecycleChoreographer"/>.
/// </summary>
public readonly record struct LifecycleFrameState(
    float ContentOpacity,
    bool ShowInstrumentalIndicator,
    float InstrumentalOpacity,
    float InstrumentalPulsePhase,
    string? OffsetToastText,
    float OffsetToastOpacity,
    bool IsAnimating)
{
    public bool ShouldRenderLyrics => ContentOpacity > 0.004f;
    public bool ShouldRenderToast => OffsetToastOpacity > 0.004f && !string.IsNullOrEmpty(OffsetToastText);
}

/// <summary>
/// Implements MOTION_AND_STYLES.md section 8.1 playback lifecycle choreography:
/// - Pause dim (300ms -&gt; 0.62 opacity, hold until 8.0s paused, then 450ms fade to 0.00)
/// - Strict suppression of PlainOnly / Loading / NotFound / Error so unsynced lines never freeze
/// - Track change crossfade on TrackGeneration change
/// - Instrumental gap fade + 450ms vocal pre-wake + high-contrast 3-dot indicator (suppressed on post-song outro)
/// - Transient 1.2s high-contrast tabular-mono timing offset toast HUD
/// </summary>
public sealed class PlaybackLifecycleChoreographer : IDisposable
{
    public const float PauseDimDurationSeconds = 0.30f;
    public const float PauseHoldLimitSeconds = 8.00f;
    public const float PauseAutoHideFadeSeconds = 0.45f;
    public const float PauseDimOpacity = 0.62f;

    public const float NormalFadeRatePerSec = 1f / 0.18f;
    public const float ReducedMotionFadeRatePerSec = 1f / 0.14f;
    public const double PreWakeWindowMilliseconds = 450.0;
    public const float InstrumentalPulsePeriodSeconds = 2.2f;

    public const double OffsetToastHoldSeconds = 1.20;
    public const double OffsetToastFadeOutSeconds = 0.18;
    public const double OffsetToastFadeInSeconds = 0.10;

    private static readonly RgbColor ToastHighContrastText = new(0xF8, 0xFA, 0xFC); // #F8FAFC (>= 15:1 on #D90B0C16)
    private static readonly SKColor ToastBackdropBase = new(0x0B, 0x0C, 0x16, 0xD9); // #D90B0C16
    private static readonly SKColor DotHaloBase = new(0x05, 0x05, 0x10, 0xB8);       // #B8050510

    private readonly SKPaint _toastBgPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _toastAccentBarPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _toastBorderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f };
    private readonly SKPaint _dotPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _dotHaloPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    private float _contentOpacity;
    private float _instrumentalOpacity;
    private float _trackFadeIn = 1.0f;
    private float _pausedElapsedSec;
    private float _pulseAccumulatorSec;
    private long _lastTrackGeneration = -1;
    private bool _wasPaused;
    private bool _disposed;

    public void Reset()
    {
        _contentOpacity = 0f;
        _instrumentalOpacity = 0f;
        _trackFadeIn = 1.0f;
        _pausedElapsedSec = 0f;
        _pulseAccumulatorSec = 0f;
        _lastTrackGeneration = -1;
        _wasPaused = false;
    }

    public LifecycleFrameState Advance(in FrameRenderPacket packet)
    {
        var dt = Math.Clamp(float.IsFinite(packet.DeltaSeconds) ? packet.DeltaSeconds : 0f, 0f, SpringState.MaxDeltaSeconds);
        var snapshot = packet.PlaybackSnapshot;
        if (snapshot is null)
        {
            var hasFallbackContent = !string.IsNullOrWhiteSpace(packet.SceneSnapshot.DiagnosticHeadline) ||
                                     packet.SceneSnapshot.Document is not null;
            _contentOpacity = hasFallbackContent ? 1f : 0f;
            return new LifecycleFrameState(
                ContentOpacity: _contentOpacity,
                ShowInstrumentalIndicator: false,
                InstrumentalOpacity: 0f,
                InstrumentalPulsePhase: 0f,
                OffsetToastText: null,
                OffsetToastOpacity: 0f,
                IsAnimating: false);
        }

        var fadeRate = packet.ReducedMotion ? ReducedMotionFadeRatePerSec : NormalFadeRatePerSec;
        var utcNow = packet.UtcNow ?? DateTimeOffset.UtcNow;

        // Track change crossfade pulse (only when switching from a previously displayed track)
        if (_lastTrackGeneration != snapshot.TrackGeneration)
        {
            var hadPreviousTrack = _lastTrackGeneration >= 0;
            _lastTrackGeneration = snapshot.TrackGeneration;
            _trackFadeIn = hadPreviousTrack ? 0f : 1f;
            _pausedElapsedSec = 0f;
        }

        var trackFadeRate = packet.ReducedMotion ? (1f / 0.14f) : (1f / 0.16f);
        var trackFading = _trackFadeIn < 1f;
        if (trackFading)
        {
            _trackFadeIn = Math.Min(1f, _trackFadeIn + (dt * trackFadeRate));
        }

        // Strict Synced-only gate: PlainOnly / Loading / NotFound / Instrumental / Error never freeze Line 1
        var hasSyncedLyrics = snapshot.Document is not null && snapshot.LyricsStatus == LyricsStatus.Synced;
        var (toastText, toastOpacity, toastAnimating) = packet.IsCompactCard
            ? (null, 0f, false)
            : EvaluateOffsetToast(snapshot, hasSyncedLyrics, utcNow);

        var surfaceAllowsLyrics = packet.SurfaceMode is StageRenderSurfaceMode.GalleryCard or StageRenderSurfaceMode.StageCanvas ||
                                  snapshot.IsLyricsVisible;
        var hasRenderableLyrics = surfaceAllowsLyrics &&
                                  hasSyncedLyrics &&
                                  snapshot.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Paused;

        if (!hasRenderableLyrics)
        {
            _wasPaused = false;
            _pausedElapsedSec = 0f;
            _contentOpacity = MoveToward(_contentOpacity, 0f, dt * fadeRate);
            _instrumentalOpacity = MoveToward(_instrumentalOpacity, 0f, dt * fadeRate);

            var stillFading = _contentOpacity > 0.004f || _instrumentalOpacity > 0.004f || toastAnimating;
            return new LifecycleFrameState(
                ContentOpacity: _contentOpacity,
                ShowInstrumentalIndicator: _instrumentalOpacity > 0.004f,
                InstrumentalOpacity: _instrumentalOpacity,
                InstrumentalPulsePhase: 0f,
                OffsetToastText: toastText,
                OffsetToastOpacity: toastOpacity,
                IsAnimating: stillFading);
        }

        // Pause dim (300ms -> 0.62) and 8.0s idle fade-out -> 0.00
        var isPaused = !snapshot.IsDemoMode && snapshot.PlaybackState == MediaPlaybackState.Paused;
        float pauseMultiplier;
        bool pauseCountdownActive;

        if (isPaused)
        {
            if (snapshot.PausedSinceUtc is { } pausedSince)
            {
                _pausedElapsedSec = (float)Math.Max(0.0, (utcNow - pausedSince).TotalSeconds);
            }
            else
            {
                _pausedElapsedSec = _wasPaused ? _pausedElapsedSec + dt : dt;
            }

            _wasPaused = true;
            pauseMultiplier = EvaluatePauseOpacity(_pausedElapsedSec, out pauseCountdownActive);
        }
        else
        {
            _wasPaused = false;
            _pausedElapsedSec = 0f;
            pauseMultiplier = 1.0f;
            pauseCountdownActive = false;
        }

        // Instrumental gap handling + 450ms vocal pre-wake (suppress dots on post-song outro)
        var slice = snapshot.ActiveSlice;
        var inPreWake = slice.TimeUntilNextLine is { } untilNext &&
                        untilNext.TotalMilliseconds >= 0.0 &&
                        untilNext.TotalMilliseconds <= PreWakeWindowMilliseconds;

        var inSuppressedInstrumental = slice.IsInInstrumentalGap && !inPreWake;
        var showInstrumentalDots = inSuppressedInstrumental &&
                                   !slice.IsPostSongOutro &&
                                   !snapshot.Settings.HideDuringInstrumentals &&
                                   !isPaused;

        var targetLyricAlpha = inSuppressedInstrumental
            ? 0f
            : pauseMultiplier * Ease.Enter(_trackFadeIn);

        var targetInstrumentalAlpha = showInstrumentalDots ? 1.0f : 0.0f;

        if (isPaused)
        {
            _contentOpacity = targetLyricAlpha;
        }
        else
        {
            _contentOpacity = MoveToward(_contentOpacity, targetLyricAlpha, dt * fadeRate);
        }

        _instrumentalOpacity = MoveToward(_instrumentalOpacity, targetInstrumentalAlpha, dt * fadeRate);

        if (showInstrumentalDots && !packet.ReducedMotion)
        {
            _pulseAccumulatorSec = (_pulseAccumulatorSec + dt) % InstrumentalPulsePeriodSeconds;
        }

        var pulsePhase = packet.ReducedMotion
            ? 0.5f
            : _pulseAccumulatorSec / InstrumentalPulsePeriodSeconds;

        var opacityMoving = MathF.Abs(_contentOpacity - targetLyricAlpha) > 0.002f ||
                            MathF.Abs(_instrumentalOpacity - targetInstrumentalAlpha) > 0.002f;

        var isAnimating = snapshot.IsPlaying ||
                          pauseCountdownActive ||
                          opacityMoving ||
                          trackFading ||
                          toastAnimating ||
                          (showInstrumentalDots && !packet.ReducedMotion);

        return new LifecycleFrameState(
            ContentOpacity: Math.Clamp(_contentOpacity, 0f, 1f),
            ShowInstrumentalIndicator: _instrumentalOpacity > 0.004f,
            InstrumentalOpacity: Math.Clamp(_instrumentalOpacity, 0f, 1f),
            InstrumentalPulsePhase: pulsePhase,
            OffsetToastText: toastText,
            OffsetToastOpacity: toastOpacity,
            IsAnimating: isAnimating);
    }

    public static float EvaluatePauseOpacity(float pausedSeconds, out bool stillActive)
    {
        if (pausedSeconds <= 0f)
        {
            stillActive = true;
            return 1.0f;
        }

        if (pausedSeconds < PauseDimDurationSeconds)
        {
            var t = Ease.Exit(pausedSeconds / PauseDimDurationSeconds);
            stillActive = true;
            return 1.0f - ((1.0f - PauseDimOpacity) * t);
        }

        if (pausedSeconds < PauseHoldLimitSeconds)
        {
            stillActive = true;
            return PauseDimOpacity;
        }

        var fadeElapsed = pausedSeconds - PauseHoldLimitSeconds;
        if (fadeElapsed < PauseAutoHideFadeSeconds)
        {
            var t = Ease.Exit(fadeElapsed / PauseAutoHideFadeSeconds);
            stillActive = true;
            return PauseDimOpacity * (1.0f - t);
        }

        stillActive = false;
        return 0.0f;
    }

    public static string FormatOffsetToastLabel(TimeSpan offset)
    {
        var seconds = offset.TotalSeconds;
        if (Math.Abs(seconds) < 0.005)
        {
            return "Offset: 0.0s (Reset)";
        }

        var atLimit = Math.Abs(seconds) >= TimingOffsetState.MaxOffset.TotalSeconds - 0.01;
        var absStr = Math.Abs(seconds).ToString("0.0", CultureInfo.InvariantCulture);
        var direction = seconds < 0 ? "Earlier" : "Later";
        var limitTag = atLimit ? " · Limit" : string.Empty;
        return seconds < 0
            ? $"Offset: \u2212{absStr}s ({direction}{limitTag})"
            : $"Offset: +{absStr}s ({direction}{limitTag})";
    }

    public void DrawInstrumentalIndicator(in StyleRenderContext context, in LifecycleFrameState lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!lifecycle.ShowInstrumentalIndicator)
        {
            return;
        }

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(context.Packet);
        var radius = Math.Clamp(context.Packet.BaseFontSizeDip * 0.11f, 3.8f, 7.0f);
        var spacing = radius * 3.5f;
        var baseOpacity = lifecycle.InstrumentalOpacity * context.Packet.GlobalOpacity;

        // Subtle dark obsidian pill halo guarantees >= 7:1 dot contrast over pure #FFFFFF web pages
        var pillW = (spacing * 2f) + (radius * 4.4f);
        var pillH = radius * 3.8f;
        var haloRect = new SKRect(anchorX - (pillW * 0.5f), anchorY - (pillH * 0.5f), anchorX + (pillW * 0.5f), anchorY + (pillH * 0.5f));
        _dotHaloPaint.Color = LegibilityStyleSpec.ScaleAlpha(DotHaloBase, baseOpacity);
        context.Canvas.DrawRoundRect(haloRect, pillH * 0.5f, pillH * 0.5f, _dotHaloPaint);

        for (var i = -1; i <= 1; i++)
        {
            var wave = context.Packet.ReducedMotion
                ? 0.80f
                : 0.55f + (0.45f * (0.5f + (0.5f * MathF.Sin((lifecycle.InstrumentalPulsePhase * MathF.Tau) - ((i + 1) * 0.9f)))));

            var dotX = anchorX + (i * spacing);
            _dotPaint.Color = context.Packet.Palette.AccentColor1.ToSkColorWithAlpha(baseOpacity * wave);
            context.Canvas.DrawCircle(dotX, anchorY, radius, _dotPaint);
        }
    }

    public void DrawOffsetToast(in StyleRenderContext context, in LifecycleFrameState lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!lifecycle.ShouldRenderToast || string.IsNullOrEmpty(lifecycle.OffsetToastText))
        {
            return;
        }

        var canvas = context.Canvas;
        var packet = context.Packet;
        var toastFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemMono, 18f);
        var textWidth = TextShaperService.MeasureWidth(lifecycle.OffsetToastText, toastFont, context.Fonts);

        const float accentBarW = 3.5f;
        const float padLeft = 16f;
        const float padRight = 14f;
        const float padY = 8f;
        var pillW = textWidth + padLeft + padRight + accentBarW;
        var pillH = toastFont.Size + (padY * 2f);

        var (anchorX, anchorY) = StyleLayoutHelper.ComputeAnchorPoint(packet);
        var preferredY = anchorY + (packet.BaseFontSizeDip * 2.25f);
        if (preferredY + (pillH * 0.5f) > packet.ViewportHeightDip - 24f)
        {
            preferredY = Math.Max(28f + (pillH * 0.5f), anchorY - (packet.BaseFontSizeDip * 2.25f));
        }

        var left = Math.Clamp(anchorX - (pillW * 0.5f), 16f, Math.Max(16f, packet.ViewportWidthDip - pillW - 16f));
        var top = Math.Clamp(preferredY - (pillH * 0.5f), 16f, Math.Max(16f, packet.ViewportHeightDip - pillH - 16f));
        var rect = new SKRect(left, top, left + pillW, top + pillH);
        var alpha = lifecycle.OffsetToastOpacity;

        _toastBgPaint.Color = LegibilityStyleSpec.ScaleAlpha(ToastBackdropBase, alpha);
        canvas.DrawRoundRect(rect, 10f, 10f, _toastBgPaint);

        // High-contrast AccentColor1 left accent bar
        var barRect = new SKRect(rect.Left + 4f, rect.Top + 6f, rect.Left + 4f + accentBarW, rect.Bottom - 6f);
        _toastAccentBarPaint.Color = packet.Palette.AccentColor1.ToSkColorWithAlpha(alpha);
        canvas.DrawRoundRect(barRect, 2f, 2f, _toastAccentBarPaint);

        _toastBorderPaint.Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)Math.Clamp(MathF.Round(46f * alpha), 0f, 255f));
        canvas.DrawRoundRect(rect, 10f, 10f, _toastBorderPaint);

        var baselineY = rect.MidY + (toastFont.Size * 0.34f);
        context.Legibility.DrawLegibleText(
            canvas,
            lifecycle.OffsetToastText,
            rect.Left + padLeft + accentBarW,
            baselineY,
            SKTextAlign.Left,
            toastFont,
            LyricFontId.SystemMono,
            ToastHighContrastText,
            opacity: alpha,
            activeGlowColor: null,
            includeStrokeHalo: false,
            fonts: context.Fonts);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _toastBgPaint.Dispose();
        _toastAccentBarPaint.Dispose();
        _toastBorderPaint.Dispose();
        _dotPaint.Dispose();
        _dotHaloPaint.Dispose();
    }

    private static (string? Text, float Opacity, bool Animating) EvaluateOffsetToast(
        OverlayPlaybackSnapshot snapshot,
        bool hasSyncedLyrics,
        DateTimeOffset utcNow)
    {
        if (!snapshot.IsLyricsVisible || !hasSyncedLyrics || snapshot.LastOffsetNudgeUtc is not { } nudgeUtc)
        {
            return (null, 0f, false);
        }

        var elapsedSec = (utcNow - nudgeUtc).TotalSeconds;
        var totalDuration = OffsetToastHoldSeconds + OffsetToastFadeOutSeconds;
        if (elapsedSec < 0.0 || elapsedSec >= totalDuration)
        {
            return (null, 0f, false);
        }

        var text = FormatOffsetToastLabel(snapshot.TimingOffset.Offset);
        if (elapsedSec < OffsetToastFadeInSeconds)
        {
            var fadeIn = Ease.Enter((float)(elapsedSec / OffsetToastFadeInSeconds));
            return (text, fadeIn, true);
        }

        if (elapsedSec <= OffsetToastHoldSeconds)
        {
            return (text, 1.0f, true);
        }

        var fadeOut = 1.0f - Ease.Exit((float)((elapsedSec - OffsetToastHoldSeconds) / OffsetToastFadeOutSeconds));
        return (text, Math.Clamp(fadeOut, 0f, 1f), true);
    }

    private static float MoveToward(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
        {
            return target;
        }

        return current + (MathF.Sign(target - current) * maxDelta);
    }
}
