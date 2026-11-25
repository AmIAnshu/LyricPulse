using System.Globalization;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Motion;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Style #10 — Messages: hyper-clean floating phone-screen chat thread locked intentionally to
/// <c>Segoe UI Variable</c> (<see cref="LyricFontId.SystemUi"/>) and high-contrast chat tokens
/// (<c>#0066D6</c> outgoing / <c>#222433</c> incoming), featuring alternating sender cadence,
/// <c>Spring.BubblePush</c> entrance, subtle intra-bubble karaoke highlight, and a pre-arrival
/// 3-dot typing indicator pill. Zero post-process shaders.
/// </summary>
public sealed class MessagesStyleRenderer : IKineticStyleRenderer
{
    public static readonly SKColor ThreadBackdropColor = new(0x10, 0x11, 0x1A, 0xC8); // #C810111A
    public static readonly SKColor ThreadBorderColor = new(0xFF, 0xFF, 0xFF, 0x20);   // #20FFFFFF
    public static readonly SKColor OutgoingTopColor = new(0x0B, 0x74, 0xE8, 0xFF);    // #0B74E8
    public static readonly SKColor OutgoingBottomColor = new(0x00, 0x5E, 0xC7, 0xFF); // #005EC7
    public static readonly SKColor IncomingBgColor = new(0x22, 0x24, 0x33, 0xF5);     // #F5222433
    public static readonly SKColor IncomingBorderColor = new(0xFF, 0xFF, 0xFF, 0x18);
    public static readonly SKColor LiveGreenDot = new(0x22, 0xC5, 0x5E, 0xFF);        // #22C55E
    public static readonly SKColor HeaderPrimaryColor = new(0xF8, 0xFA, 0xFC, 0xFF);  // #F8FAFC
    public static readonly SKColor HeaderSecondaryColor = new(0x94, 0xA3, 0xB8, 0xFF);// #94A3B8
    public static readonly SKColor TypingDotColor = new(0xCB, 0xD5, 0xE1, 0xFF);      // #CBD5E1

    private static readonly SKColor[] OutgoingGradientColors = [OutgoingTopColor, OutgoingBottomColor];
    private static readonly float[] OutgoingGradientStops = [0f, 1f];

    private readonly SKPaint _cardFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _cardBorderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f };
    private readonly SKPaint _cardShadowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _bubbleFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _bubbleBorderPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f };
    private readonly SKPaint _textPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _dotPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKRoundRect _clipRoundRect = new();
    private readonly SKRoundRect _bubbleRoundRect = new();
    private readonly SKImageFilter _cardShadowFilter = SKImageFilter.CreateDropShadowOnly(0f, 10f, 18f, 18f, new SKColor(0, 0, 0, 115));

    private SpringState _bubblePushY = SpringState.AtRest(0f);
    private SpringState _newBubbleScale = SpringState.AtRest(1.0f);
    private float _fadeIn = 1.0f;
    private double _typingPhaseSec;
    private int _lastLineIndex = -1;
    private bool _disposed;

    public OverlayStyleId StyleId => OverlayStyleId.Messages;

    public void ResetState()
    {
        _bubblePushY = SpringState.AtRest(0f);
        _newBubbleScale = SpringState.AtRest(1.0f);
        _fadeIn = 1.0f;
        _typingPhaseSec = 0.0;
        _lastLineIndex = -1;
    }

    public bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle)
    {
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        if (slice.CurrentLine is null)
        {
            return false;
        }

        var dt = Math.Clamp(packet.DeltaSeconds, 0f, SpringState.MaxDeltaSeconds);
        if (slice.ActiveLineIndex != _lastLineIndex)
        {
            var wasInitialized = _lastLineIndex >= 0;
            _lastLineIndex = slice.ActiveLineIndex;

            if (packet.ReducedMotion)
            {
                _bubblePushY = SpringState.AtRest(0f);
                _newBubbleScale = SpringState.AtRest(1.0f);
                _fadeIn = 0f;
            }
            else
            {
                if (wasInitialized)
                {
                    _bubblePushY = new SpringState(Value: 26f, Velocity: 0f, Target: 0f);
                    _newBubbleScale = new SpringState(Value: 0.86f, Velocity: 0f, Target: 1.00f);
                }

                _fadeIn = 1f;
            }
        }

        if (packet.ReducedMotion)
        {
            _bubblePushY = SpringState.AtRest(0f);
            _newBubbleScale = SpringState.AtRest(1.0f);
            _fadeIn = Math.Min(1f, _fadeIn + (dt / 0.14f));
            return _fadeIn < 0.999f;
        }

        _typingPhaseSec = (_typingPhaseSec + dt) % 0.9;
        _bubblePushY = _bubblePushY.Step(SpringTokens.BubblePush, dt);
        _newBubbleScale = _newBubbleScale.Step(SpringTokens.BubblePush, dt);

        var showTyping = IsTypingCueVisible(slice);
        return !_bubblePushY.IsAtRest || !_newBubbleScale.IsAtRest || showTyping;
    }

    public void Render(in StyleRenderContext context, in LifecycleFrameState lifecycle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var packet = context.Packet;
        var slice = StyleLayoutHelper.ResolveSlice(packet);
        if (slice.CurrentLine is null)
        {
            return;
        }

        var isCompact = packet.IsCompactCard;
        var heroMult = packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas
            ? Math.Clamp(packet.SafeHeroScale * 0.72f, 1.0f, 1.65f)
            : 1.0f;

        var compactMaxW = Math.Max(120f, Math.Min(packet.ViewportWidthDip - 12f, 280f));
        var compactMaxH = Math.Max(90f, Math.Min(packet.ViewportHeightDip - 12f, 220f));
        var stageMaxW = Math.Max(160f, Math.Min(packet.ViewportWidthDip - 32f, 520f));
        var stageMaxH = Math.Max(140f, Math.Min(packet.ViewportHeightDip - 32f, 640f));

        var threadW = isCompact
            ? Math.Clamp(packet.ViewportWidthDip * 0.84f, Math.Min(150f, compactMaxW), compactMaxW)
            : Math.Clamp(packet.ViewportWidthDip * 0.24f * heroMult, Math.Min(310f, stageMaxW), stageMaxW);
        var threadH = isCompact
            ? Math.Clamp(packet.ViewportHeightDip * 0.86f, Math.Min(110f, compactMaxH), compactMaxH)
            : Math.Clamp(packet.ViewportHeightDip * 0.48f * heroMult, Math.Min(320f, stageMaxH), stageMaxH);

        // Lower-right on DesktopOverlay (per MOTION_AND_STYLES.md), centered on StageCanvas / GalleryCard
        var (anchorX, anchorY) = packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay
            ? (packet.ViewportWidthDip - (threadW * 0.5f) - Math.Max(24f, packet.ViewportWidthDip * 0.04f),
               packet.ViewportHeightDip - (threadH * 0.5f) - Math.Max(24f, packet.ViewportHeightDip * 0.08f))
            : StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);

        var cardRect = StyleLayoutHelper.ClampLayerRectToViewport(anchorX, anchorY, threadW, threadH, packet.ViewportWidthDip, packet.ViewportHeightDip);
        var corner = isCompact ? 14f : 22f;

        // Soft elevation shadow + phone glass card
        _cardShadowPaint.Color = SKColors.Black;
        _cardShadowPaint.ImageFilter = _cardShadowFilter;
        try
        {
            context.Canvas.DrawRoundRect(cardRect, corner, corner, _cardShadowPaint);
        }
        finally
        {
            _cardShadowPaint.ImageFilter = null;
        }

        _cardFillPaint.Color = ThreadBackdropColor;
        context.Canvas.DrawRoundRect(cardRect, corner, corner, _cardFillPaint);

        _cardBorderPaint.Color = ThreadBorderColor;
        context.Canvas.DrawRoundRect(cardRect, corner, corner, _cardBorderPaint);

        var headerH = isCompact ? 24f : 40f;
        var bodyFontSize = isCompact ? 11f : Math.Clamp(15.5f * heroMult, 14f, 24f);
        var metaFontSize = isCompact ? 8.5f : Math.Clamp(11f * heroMult, 10f, 15f);
        var bodyFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemUi, bodyFontSize, enforceMinOverlayFloor: false);
        var metaFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemUi, metaFontSize, enforceMinOverlayFloor: false);

        var clipSave = context.Canvas.Save();
        try
        {
            var radii = new[] { new SKPoint(corner, corner), new SKPoint(corner, corner), new SKPoint(corner, corner), new SKPoint(corner, corner) };
            _clipRoundRect.SetRectRadii(cardRect, radii);
            context.Canvas.ClipRoundRect(_clipRoundRect, antialias: true);

            DrawThreadHeader(context.Canvas, cardRect, headerH, bodyFont, metaFont, packet, isCompact);

            var threadClip = new SKRect(cardRect.Left, cardRect.Top + headerH, cardRect.Right, cardRect.Bottom);
            context.Canvas.ClipRect(threadClip);

            var padX = isCompact ? 8f : 14f;
            var pushY = packet.ReducedMotion ? 0f : _bubblePushY.Value;
            var bottomY = cardRect.Bottom - (isCompact ? 8f : 14f) + pushY;

            // Optional pre-arrival 3-dot typing indicator at bottom
            if (IsTypingCueVisible(slice))
            {
                var nextOutgoing = IsOutgoingSide(slice.ActiveLineIndex + 1);
                var pillH = isCompact ? 18f : 28f;
                var pillW = isCompact ? 34f : 48f;
                var pillX = nextOutgoing ? (cardRect.Right - padX - pillW) : (cardRect.Left + padX);
                var pillRect = new SKRect(pillX, bottomY - pillH, pillX + pillW, bottomY);
                DrawTypingIndicatorPill(context.Canvas, pillRect, packet.ReducedMotion);
                bottomY -= pillH + (isCompact ? 5f : 10f);
            }

            // Timestamp micro-caption under newest bubble
            var timestampStr = FormatTimestamp(slice.CurrentLine.StartTime);
            var activeOutgoing = IsOutgoingSide(slice.ActiveLineIndex);
            _textPaint.Color = HeaderSecondaryColor.WithAlpha(205);
            context.Canvas.DrawText(
                timestampStr,
                activeOutgoing ? cardRect.Right - padX - 4f : cardRect.Left + padX + 4f,
                bottomY - 2f,
                activeOutgoing ? SKTextAlign.Right : SKTextAlign.Left,
                metaFont,
                _textPaint);
            bottomY -= metaFontSize + (isCompact ? 4f : 7f);

            // Collect up to 4 visible bubbles bottom-up (active + up to 3 settled history lines)
            var history = CollectVisibleLines(packet, slice, maxCount: isCompact ? 3 : 4);
            var maxBubbleW = (cardRect.Width - (padX * 2f)) * 0.82f;
            var fadeAlpha = packet.ReducedMotion ? (0.90f + (0.10f * Ease.Highlight(_fadeIn))) : 1.0f;

            for (var i = history.Count - 1; i >= 0; i--)
            {
                var line = history[i];
                var isNewest = i == history.Count - 1;
                var outgoing = IsOutgoingSide(line.Index);
                var bubbleScale = isNewest && !packet.ReducedMotion ? _newBubbleScale.Value : 1.0f;
                var ageFade = Math.Clamp(1.0f - ((history.Count - 1 - i) * 0.14f), 0.55f, 1.0f);

                var bubbleH = DrawMessageBubble(
                    in context,
                    line,
                    slice,
                    bodyFont,
                    metaFont,
                    cardRect,
                    padX,
                    maxBubbleW,
                    bottomY,
                    outgoing,
                    isNewest,
                    bubbleScale,
                    ageFade * fadeAlpha,
                    isCompact);

                bottomY -= bubbleH + (isCompact ? 5f : 9f);
                if (bottomY < threadClip.Top - 24f)
                {
                    break;
                }
            }
        }
        finally
        {
            context.Canvas.RestoreToCount(clipSave);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clipRoundRect.Dispose();
        _bubbleRoundRect.Dispose();
        _cardShadowFilter.Dispose();
        _cardFillPaint.Dispose();
        _cardBorderPaint.Dispose();
        _cardShadowPaint.Dispose();
        _bubbleFillPaint.Dispose();
        _bubbleBorderPaint.Dispose();
        _textPaint.Dispose();
        _dotPaint.Dispose();
    }

    internal static bool IsOutgoingSide(int lineIndex) => ((Math.Max(0, lineIndex) / 2) % 2) == 0;

    private void DrawThreadHeader(
        SKCanvas canvas,
        SKRect cardRect,
        float headerH,
        SKFont bodyFont,
        SKFont metaFont,
        FrameRenderPacket packet,
        bool isCompact)
    {
        var padX = isCompact ? 10f : 14f;
        var cy = cardRect.Top + (headerH * 0.5f);
        var avatarR = isCompact ? 6.5f : 11f;
        var avatarX = cardRect.Left + padX + avatarR;

        _dotPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.95f);
        canvas.DrawCircle(avatarX, cy, avatarR, _dotPaint);

        // Live green presence dot
        _dotPaint.Color = LiveGreenDot;
        canvas.DrawCircle(avatarX + (avatarR * 0.72f), cy + (avatarR * 0.68f), isCompact ? 2.4f : 3.5f, _dotPaint);

        var title = packet.SceneSnapshot.Track?.RawTitle ?? "Neon Horizon";
        var textX = avatarX + avatarR + (isCompact ? 6f : 10f);
        _textPaint.Color = HeaderPrimaryColor;
        canvas.DrawText(
            StyleLayoutHelper.SliceByGraphemes(title, isCompact ? 16 : 26),
            textX,
            cy + (isCompact ? 3.5f : 1f),
            SKTextAlign.Left,
            isCompact ? metaFont : bodyFont,
            _textPaint);

        if (!isCompact)
        {
            var artist = packet.SceneSnapshot.Track?.RawArtist ?? "Now Playing";
            _textPaint.Color = HeaderSecondaryColor;
            canvas.DrawText(
                StyleLayoutHelper.SliceByGraphemes(artist, 28),
                textX,
                cy + 14f,
                SKTextAlign.Left,
                metaFont,
                _textPaint);
        }

        // Subtle header divider line
        _cardBorderPaint.Color = new SKColor(0xFF, 0xFF, 0xFF, 18);
        canvas.DrawLine(cardRect.Left + 10f, cardRect.Top + headerH, cardRect.Right - 10f, cardRect.Top + headerH, _cardBorderPaint);
    }

    private float DrawMessageBubble(
        in StyleRenderContext context,
        LyricLine line,
        ActiveLyricSlice slice,
        SKFont bodyFont,
        SKFont metaFont,
        SKRect cardRect,
        float padX,
        float maxBubbleW,
        float bottomY,
        bool outgoing,
        bool isNewest,
        float scale,
        float opacity,
        bool isCompact)
    {
        var bPadX = isCompact ? 8f : 13f;
        var bPadY = isCompact ? 5f : 9f;
        var lineH = bodyFont.Size * 1.32f;
        var maxTextW = Math.Max(40f, maxBubbleW - (bPadX * 2f));

        var wrappedLines = WrapLineText(line.RawText, bodyFont, maxTextW, context.Fonts);
        var widest = 0f;
        foreach (var wl in wrappedLines)
        {
            widest = Math.Max(widest, TextShaperService.MeasureWidth(wl, bodyFont, context.Fonts));
        }

        var subText = isNewest ? StyleLayoutHelper.ResolveSecondaryLineText(line, context.Packet.SecondaryLineMode) : null;
        var hasSub = !string.IsNullOrWhiteSpace(subText);
        if (hasSub && subText is not null)
        {
            widest = Math.Max(widest, Math.Min(maxTextW, TextShaperService.MeasureWidth(subText, metaFont, context.Fonts)));
        }

        var bubbleW = Math.Clamp(widest + (bPadX * 2f), isCompact ? 42f : 64f, maxBubbleW);
        var bubbleH = (wrappedLines.Count * lineH) + (bPadY * 2f) + (hasSub ? (metaFont.Size * 1.3f) : 0f);

        var left = outgoing ? (cardRect.Right - padX - bubbleW) : (cardRect.Left + padX);
        var rect = new SKRect(left, bottomY - bubbleH, left + bubbleW, bottomY);

        var pivotX = outgoing ? rect.Right : rect.Left;
        var pivotY = rect.Bottom;
        var save = context.Canvas.Save();
        try
        {
            if (MathF.Abs(scale - 1f) > 0.002f)
            {
                context.Canvas.Translate(pivotX, pivotY);
                context.Canvas.Scale(scale, scale);
                context.Canvas.Translate(-pivotX, -pivotY);
            }

            var r = isCompact ? 11f : 17f;
            var tailR = isCompact ? 4f : 5f;
            var corners = outgoing
                ? new[] { new SKPoint(r, r), new SKPoint(r, r), new SKPoint(tailR, tailR), new SKPoint(r, r) }
                : new[] { new SKPoint(r, r), new SKPoint(r, r), new SKPoint(r, r), new SKPoint(tailR, tailR) };
            _bubbleRoundRect.SetRectRadii(rect, corners);

            if (outgoing)
            {
                using var grad = SKShader.CreateLinearGradient(
                    new SKPoint(rect.MidX, rect.Top),
                    new SKPoint(rect.MidX, rect.Bottom),
                    OutgoingGradientColors,
                    OutgoingGradientStops,
                    SKShaderTileMode.Clamp);
                _bubbleFillPaint.Shader = grad;
                _bubbleFillPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(MathF.Round(255f * opacity), 0f, 255f));
                try
                {
                    context.Canvas.DrawRoundRect(_bubbleRoundRect, _bubbleFillPaint);
                }
                finally
                {
                    _bubbleFillPaint.Shader = null;
                }
            }
            else
            {
                _bubbleFillPaint.Color = IncomingBgColor.WithAlpha((byte)Math.Clamp(MathF.Round(IncomingBgColor.Alpha * opacity), 0f, 255f));
                context.Canvas.DrawRoundRect(_bubbleRoundRect, _bubbleFillPaint);

                _bubbleBorderPaint.Color = IncomingBorderColor.WithAlpha((byte)Math.Clamp(MathF.Round(IncomingBorderColor.Alpha * opacity), 0f, 255f));
                context.Canvas.DrawRoundRect(_bubbleRoundRect, _bubbleBorderPaint);
            }

            var textY = rect.Top + bPadY + (bodyFont.Size * 0.92f);
            var textAlpha = isNewest
                ? (0.72f + (0.28f * (float)Math.Clamp(slice.LineProgress, 0.0, 1.0))) * opacity
                : opacity;
            _textPaint.Color = (outgoing ? SKColors.White : HeaderPrimaryColor)
                .WithAlpha((byte)Math.Clamp(MathF.Round(255f * textAlpha), 0f, 255f));

            foreach (var wl in wrappedLines)
            {
                context.Canvas.DrawText(wl, rect.Left + bPadX, textY, SKTextAlign.Left, bodyFont, _textPaint);
                textY += lineH;
            }

            if (hasSub && subText is not null)
            {
                _textPaint.Color = (outgoing ? new SKColor(0xE0, 0xF2, 0xFE, 210) : HeaderSecondaryColor)
                    .WithAlpha((byte)Math.Clamp(MathF.Round(210f * opacity), 0f, 255f));
                context.Canvas.DrawText(
                    StyleLayoutHelper.SliceByGraphemes(subText, 36),
                    rect.Left + bPadX,
                    textY - (lineH * 0.15f),
                    SKTextAlign.Left,
                    metaFont,
                    _textPaint);
            }
        }
        finally
        {
            context.Canvas.RestoreToCount(save);
        }

        return bubbleH;
    }

    private void DrawTypingIndicatorPill(SKCanvas canvas, SKRect rect, bool reducedMotion)
    {
        var r = rect.Height * 0.5f;
        _bubbleFillPaint.Color = IncomingBgColor;
        canvas.DrawRoundRect(rect, r, r, _bubbleFillPaint);

        var dotR = Math.Clamp(rect.Height * 0.14f, 2.0f, 3.6f);
        var spacing = rect.Width * 0.24f;
        for (var i = 0; i < 3; i++)
        {
            var dx = rect.MidX + ((i - 1) * spacing);
            var wave = reducedMotion
                ? 0f
                : MathF.Sin((float)((_typingPhaseSec / 0.9) * Math.PI * 2.0) - (i * 0.85f)) * 2.4f;
            _dotPaint.Color = TypingDotColor.WithAlpha((byte)(reducedMotion ? 200 : 175 + (int)(MathF.Max(0f, -wave) * 30f)));
            canvas.DrawCircle(dx, rect.MidY + wave, dotR, _dotPaint);
        }
    }

    private static bool IsTypingCueVisible(ActiveLyricSlice slice) =>
        slice.NextLine is not null &&
        (slice.TimeUntilNextLine is { TotalMilliseconds: > 0 and <= 450 } || slice.LineProgress >= 0.82);

    private static List<LyricLine> CollectVisibleLines(FrameRenderPacket packet, ActiveLyricSlice slice, int maxCount)
    {
        var result = new List<LyricLine>(maxCount);
        var doc = packet.SceneSnapshot.Document;
        if (doc is not null && !doc.Lines.IsEmpty && slice.ActiveLineIndex >= 0)
        {
            var start = Math.Max(0, slice.ActiveLineIndex - (maxCount - 1));
            for (var i = start; i <= slice.ActiveLineIndex && i < doc.Lines.Length; i++)
            {
                result.Add(doc.Lines[i]);
            }

            return result;
        }

        if (slice.PreviousLine is not null)
        {
            result.Add(slice.PreviousLine);
        }

        if (slice.CurrentLine is not null)
        {
            result.Add(slice.CurrentLine);
        }

        return result;
    }

    private static List<string> WrapLineText(string text, SKFont font, float maxWidth, FontAssetRegistry fonts)
    {
        var lines = new List<string>(2);
        if (string.IsNullOrWhiteSpace(text))
        {
            return lines;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        foreach (var part in parts)
        {
            var candidate = string.IsNullOrEmpty(current) ? part : $"{current} {part}";
            if (!string.IsNullOrEmpty(current) && TextShaperService.MeasureWidth(candidate, font, fonts) > maxWidth)
            {
                lines.Add(current);
                current = part;
            }
            else
            {
                current = candidate;
            }
        }

        if (!string.IsNullOrEmpty(current))
        {
            lines.Add(current);
        }

        return lines;
    }

    private static string FormatTimestamp(TimeSpan time)
    {
        var clamped = time < TimeSpan.Zero ? TimeSpan.Zero : time;
        return $"{(int)clamped.TotalMinutes}:{clamped.Seconds:00}";
    }
}
