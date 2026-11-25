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
/// Style #10 — Messages / Native Design (Verci-inspired): frameless floating iMessage-style
/// conversation thread centered on Stage Canvas (frosted glass container on transparent Desktop Overlay),
/// <c>Today HH:mm</c> micro-header, vibrant blue outgoing &amp; obsidian frosted incoming bubbles,
/// <c>Spring.BubblePush</c> entrance, HarfBuzz multi-script text, and zero per-frame allocations.
/// </summary>
public sealed class MessagesStyleRenderer : IKineticStyleRenderer
{
    public static readonly SKColor ThreadBackdropColor = new(0x10, 0x11, 0x1A, 0xC8); // #C810111A
    public static readonly SKColor ThreadBorderColor = new(0xFF, 0xFF, 0xFF, 0x20);   // #20FFFFFF
    public static readonly SKColor OutgoingTopColor = new(0x0E, 0x84, 0xFF, 0xFF);    // #0E84FF
    public static readonly SKColor OutgoingBottomColor = new(0x00, 0x62, 0xD1, 0xFF); // #0062D1
    public static readonly SKColor IncomingBgColor = new(0x22, 0x24, 0x33, 0xF2);     // #F2222433
    public static readonly SKColor IncomingBorderColor = new(0xFF, 0xFF, 0xFF, 0x1E);
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
    private readonly SKPaint _dotPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKRoundRect _clipRoundRect = new();
    private readonly SKRoundRect _bubbleRoundRect = new();
    private readonly SKPoint[] _clipRadii = new SKPoint[4];
    private readonly SKPoint[] _bubbleRadii = new SKPoint[4];
    private readonly List<LyricLine> _visibleLinesScratch = new(6);
    private readonly List<string> _wrappedLinesScratch = new(6);
    private readonly SKImageFilter _cardShadowFilter = SKImageFilter.CreateDropShadowOnly(0f, 10f, 18f, 18f, new SKColor(0, 0, 0, 105));

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
        var isFloatingDesktop = packet.SurfaceMode == StageRenderSurfaceMode.DesktopOverlay;
        var heroMult = packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas
            ? Math.Clamp(packet.SafeHeroScale * 0.78f, 1.0f, 1.75f)
            : 1.0f;

        var compactMaxW = Math.Max(120f, Math.Min(packet.ViewportWidthDip - 12f, 280f));
        var compactMaxH = Math.Max(90f, Math.Min(packet.ViewportHeightDip - 12f, 220f));
        var stageMaxW = Math.Max(180f, Math.Min(packet.ViewportWidthDip - 32f, 580f));
        var stageMaxH = Math.Max(150f, Math.Min(packet.ViewportHeightDip - 32f, 640f));

        var threadW = isCompact
            ? Math.Clamp(packet.ViewportWidthDip * 0.86f, Math.Min(150f, compactMaxW), compactMaxW)
            : Math.Clamp(packet.ViewportWidthDip * 0.28f * heroMult, Math.Min(340f, stageMaxW), stageMaxW);
        var threadH = isCompact
            ? Math.Clamp(packet.ViewportHeightDip * 0.86f, Math.Min(110f, compactMaxH), compactMaxH)
            : Math.Clamp(packet.ViewportHeightDip * 0.48f * heroMult, Math.Min(300f, stageMaxH), stageMaxH);

        var (anchorX, anchorY) = isFloatingDesktop
            ? (packet.ViewportWidthDip - (threadW * 0.5f) - Math.Max(24f, packet.ViewportWidthDip * 0.04f),
               packet.ViewportHeightDip - (threadH * 0.5f) - Math.Max(24f, packet.ViewportHeightDip * 0.08f))
            : StyleLayoutHelper.ComputeAnchorPoint(packet, 0.50f, 0.50f);

        var cardRect = StyleLayoutHelper.ClampLayerRectToViewport(anchorX, anchorY, threadW, threadH, packet.ViewportWidthDip, packet.ViewportHeightDip);
        var corner = isCompact ? 14f : 22f;

        // Frameless floating chat bubbles on StageCanvas / GalleryCard (per Verci Native Design!),
        // with clean glass phone container on transparent DesktopOverlay.
        if (isFloatingDesktop)
        {
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
        }

        var headerH = isCompact ? 20f : (isFloatingDesktop ? 42f : 28f);
        var bodyFontSize = isCompact ? 11.5f : Math.Clamp(16f * heroMult, 14f, 25f);
        var metaFontSize = isCompact ? 8.5f : Math.Clamp(11.5f * heroMult, 10f, 15f);
        var bodyFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemUi, bodyFontSize, enforceMinOverlayFloor: false);
        var metaFont = context.Fonts.GetOrCreateFont(LyricFontId.SystemUi, metaFontSize, enforceMinOverlayFloor: false);

        var clipSave = context.Canvas.Save();
        try
        {
            SetUniformRadii(_clipRadii, corner);
            _clipRoundRect.SetRectRadii(cardRect, _clipRadii);
            context.Canvas.ClipRoundRect(_clipRoundRect, antialias: true);

            if (isFloatingDesktop)
            {
                DrawThreadHeader(context, cardRect, headerH, bodyFont, metaFont, packet, isCompact);
            }
            else if (!isCompact)
            {
                // Verci-style minimal "Today HH:mm" micro-caption centered cleanly above the bubbles
                var stamp = $"Today {FormatTimestamp(slice.CurrentLine.StartTime)}";
                context.Legibility.DrawLegibleText(
                    context.Canvas,
                    stamp,
                    cardRect.MidX,
                    cardRect.Top + 18f,
                    SKTextAlign.Center,
                    metaFont,
                    LyricFontId.SystemUi,
                    new RgbColor(HeaderSecondaryColor.Red, HeaderSecondaryColor.Green, HeaderSecondaryColor.Blue),
                    opacity: 0.78f,
                    includeStrokeHalo: false,
                    fonts: context.Fonts);
            }

            var threadClip = new SKRect(cardRect.Left, cardRect.Top + headerH, cardRect.Right, cardRect.Bottom);
            context.Canvas.ClipRect(threadClip);

            var padX = isCompact ? 8f : 14f;
            var pushY = packet.ReducedMotion ? 0f : _bubblePushY.Value;
            var bottomY = cardRect.Bottom - (isCompact ? 8f : 14f) + pushY;

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

            CollectVisibleLines(packet, slice, maxCount: isCompact ? 3 : 4, _visibleLinesScratch);
            var maxBubbleW = (cardRect.Width - (padX * 2f)) * 0.84f;
            var fadeAlpha = packet.ReducedMotion ? (0.90f + (0.10f * Ease.Highlight(_fadeIn))) : 1.0f;

            for (var i = _visibleLinesScratch.Count - 1; i >= 0; i--)
            {
                var line = _visibleLinesScratch[i];
                var isNewest = i == _visibleLinesScratch.Count - 1;
                var outgoing = IsOutgoingSide(line.Index);
                var bubbleScale = isNewest && !packet.ReducedMotion ? _newBubbleScale.Value : 1.0f;
                var ageFade = Math.Clamp(1.0f - ((_visibleLinesScratch.Count - 1 - i) * 0.14f), 0.55f, 1.0f);

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
        _dotPaint.Dispose();
    }

    internal static bool IsOutgoingSide(int lineIndex) => ((Math.Max(0, lineIndex) / 2) % 2) == 0;

    private void DrawThreadHeader(
        in StyleRenderContext context,
        SKRect cardRect,
        float headerH,
        SKFont bodyFont,
        SKFont metaFont,
        FrameRenderPacket packet,
        bool isCompact)
    {
        var canvas = context.Canvas;
        var padX = isCompact ? 10f : 14f;
        var cy = cardRect.Top + (headerH * 0.5f);
        var avatarR = isCompact ? 6.5f : 11f;
        var avatarX = cardRect.Left + padX + avatarR;

        _dotPaint.Color = packet.Palette.AccentColor2.ToSkColorWithAlpha(0.95f);
        canvas.DrawCircle(avatarX, cy, avatarR, _dotPaint);

        _dotPaint.Color = LiveGreenDot;
        canvas.DrawCircle(avatarX + (avatarR * 0.72f), cy + (avatarR * 0.68f), isCompact ? 2.4f : 3.5f, _dotPaint);

        var title = packet.SceneSnapshot.Track?.RawTitle ?? "Neon Horizon";
        var textX = avatarX + avatarR + (isCompact ? 6f : 10f);
        context.Legibility.DrawLegibleText(
            canvas,
            StyleLayoutHelper.SliceByGraphemes(title, isCompact ? 16 : 26),
            textX,
            cy + (isCompact ? 3.5f : 1f),
            SKTextAlign.Left,
            isCompact ? metaFont : bodyFont,
            LyricFontId.SystemUi,
            new RgbColor(HeaderPrimaryColor.Red, HeaderPrimaryColor.Green, HeaderPrimaryColor.Blue),
            opacity: 1.0f,
            includeStrokeHalo: false,
            fonts: context.Fonts,
            includeAmbientShadow: false);

        if (!isCompact)
        {
            var artist = packet.SceneSnapshot.Track?.RawArtist ?? "Now Playing";
            context.Legibility.DrawLegibleText(
                canvas,
                StyleLayoutHelper.SliceByGraphemes(artist, 28),
                textX,
                cy + 14f,
                SKTextAlign.Left,
                metaFont,
                LyricFontId.SystemUi,
                new RgbColor(HeaderSecondaryColor.Red, HeaderSecondaryColor.Green, HeaderSecondaryColor.Blue),
                opacity: 1.0f,
                includeStrokeHalo: false,
                fonts: context.Fonts,
                includeAmbientShadow: false);
        }

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
        var bPadX = isCompact ? 8f : 14f;
        var bPadY = isCompact ? 5f : 9f;
        var lineH = bodyFont.Size * 1.32f;
        var maxTextW = Math.Max(40f, maxBubbleW - (bPadX * 2f));

        WrapLineText(line.RawText, bodyFont, maxTextW, context.Fonts, _wrappedLinesScratch);
        var widest = 0f;
        for (var i = 0; i < _wrappedLinesScratch.Count; i++)
        {
            widest = Math.Max(widest, TextShaperService.MeasureWidth(_wrappedLinesScratch[i], bodyFont, context.Fonts));
        }

        var subText = isNewest ? StyleLayoutHelper.ResolveSecondaryLineText(line, context.Packet.SecondaryLineMode) : null;
        var hasSub = !string.IsNullOrWhiteSpace(subText);
        if (hasSub && subText is not null)
        {
            widest = Math.Max(widest, Math.Min(maxTextW, TextShaperService.MeasureWidth(subText, metaFont, context.Fonts)));
        }

        var bubbleW = Math.Clamp(widest + (bPadX * 2f), isCompact ? 42f : 64f, maxBubbleW);
        var bubbleH = (_wrappedLinesScratch.Count * lineH) + (bPadY * 2f) + (hasSub ? (metaFont.Size * 1.3f) : 0f);

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

            var r = isCompact ? 11f : 18f;
            var tailR = isCompact ? 4f : 5f;
            _bubbleRadii[0] = new SKPoint(r, r);
            _bubbleRadii[1] = new SKPoint(r, r);
            _bubbleRadii[2] = outgoing ? new SKPoint(tailR, tailR) : new SKPoint(r, r);
            _bubbleRadii[3] = outgoing ? new SKPoint(r, r) : new SKPoint(tailR, tailR);
            _bubbleRoundRect.SetRectRadii(rect, _bubbleRadii);

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
                ? (0.76f + (0.24f * (float)Math.Clamp(slice.LineProgress, 0.0, 1.0))) * opacity
                : opacity;
            var textRgb = outgoing
                ? new RgbColor(255, 255, 255)
                : new RgbColor(HeaderPrimaryColor.Red, HeaderPrimaryColor.Green, HeaderPrimaryColor.Blue);

            for (var i = 0; i < _wrappedLinesScratch.Count; i++)
            {
                context.Legibility.DrawLegibleText(
                    context.Canvas,
                    _wrappedLinesScratch[i],
                    rect.Left + bPadX,
                    textY,
                    SKTextAlign.Left,
                    bodyFont,
                    LyricFontId.SystemUi,
                    textRgb,
                    opacity: textAlpha,
                    includeStrokeHalo: false,
                    fonts: context.Fonts,
                    includeAmbientShadow: false);
                textY += lineH;
            }

            if (hasSub && subText is not null)
            {
                var subRgb = outgoing
                    ? new RgbColor(0xE0, 0xF2, 0xFE)
                    : new RgbColor(HeaderSecondaryColor.Red, HeaderSecondaryColor.Green, HeaderSecondaryColor.Blue);
                context.Legibility.DrawLegibleText(
                    context.Canvas,
                    StyleLayoutHelper.SliceByGraphemes(subText, 36),
                    rect.Left + bPadX,
                    textY - (lineH * 0.15f),
                    SKTextAlign.Left,
                    metaFont,
                    LyricFontId.SystemUi,
                    subRgb,
                    opacity: 0.82f * opacity,
                    includeStrokeHalo: false,
                    fonts: context.Fonts,
                    includeAmbientShadow: false);
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

    private static void CollectVisibleLines(FrameRenderPacket packet, ActiveLyricSlice slice, int maxCount, List<LyricLine> destination)
    {
        destination.Clear();
        var doc = packet.SceneSnapshot.Document;
        if (doc is not null && !doc.Lines.IsEmpty && slice.ActiveLineIndex >= 0)
        {
            var start = Math.Max(0, slice.ActiveLineIndex - (maxCount - 1));
            for (var i = start; i <= slice.ActiveLineIndex && i < doc.Lines.Length; i++)
            {
                destination.Add(doc.Lines[i]);
            }

            return;
        }

        if (slice.PreviousLine is not null)
        {
            destination.Add(slice.PreviousLine);
        }

        if (slice.CurrentLine is not null)
        {
            destination.Add(slice.CurrentLine);
        }
    }

    private static void WrapLineText(string text, SKFont font, float maxWidth, FontAssetRegistry fonts, List<string> destination)
    {
        destination.Clear();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        foreach (var part in parts)
        {
            var candidate = string.IsNullOrEmpty(current) ? part : $"{current} {part}";
            if (!string.IsNullOrEmpty(current) && TextShaperService.MeasureWidth(candidate, font, fonts) > maxWidth)
            {
                destination.Add(current);
                current = part;
            }
            else
            {
                current = candidate;
            }
        }

        if (!string.IsNullOrEmpty(current))
        {
            destination.Add(current);
        }
    }

    private static void SetUniformRadii(SKPoint[] radii, float corner)
    {
        var pt = new SKPoint(corner, corner);
        radii[0] = pt;
        radii[1] = pt;
        radii[2] = pt;
        radii[3] = pt;
    }

    private static string FormatTimestamp(TimeSpan time)
    {
        var clamped = time < TimeSpan.Zero ? TimeSpan.Zero : time;
        return $"{(int)clamped.TotalMinutes}:{clamped.Seconds:00}";
    }
}
