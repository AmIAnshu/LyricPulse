using LyricPulse.Core.Models;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Authoritative <see cref="ILyricSceneRenderer"/> coordinating <see cref="StageBackdropRenderer"/>,
/// <see cref="PlaybackLifecycleChoreographer"/>, and all 11 registered kinetic lyric styles
/// (<c>Fisheye</c>, <c>FisheyeVisual</c>, <c>Codestyle</c>, <c>CodestyleVisual</c>, <c>VisualStack</c>,
/// <c>Constellation</c>, <c>SpidersWeb</c>, <c>NodeLine</c>, <c>KineticWall</c>, <c>Messages</c>, <c>VinylGroove</c>).
/// Synchronizes cross-thread Advance/Render/Dispose, honors per-frame <c>StyleOverride</c> for
/// simultaneous Gallery previews, and preserves spring velocity across timing offset nudges.
/// </summary>
public sealed class LyricStyleRouter : ILyricSceneRenderer, IDisposable
{
    private readonly Dictionary<OverlayStyleId, IKineticStyleRenderer> _styles;
    private readonly IKineticStyleRenderer _fallbackStyle;
    private readonly PlaybackLifecycleChoreographer _lifecycle;
    private readonly StageBackdropRenderer _backdrop;
    private readonly AudioVisualizerRenderer _visualizer;
    private readonly EdgeGlowRenderer _edgeGlow;
    private readonly Lock _sync = new();

    private OverlayStyleId _lastRoutedStyle = OverlayStyleId.Fisheye;
    private long _lastTrackGeneration = -1;
    private long _lastSeekGeneration = -1;
    private long _lastOffsetGeneration = -1;
    private TimeSpan _lastEffectivePos = TimeSpan.Zero;
    private LifecycleFrameState _lastLifecycle;
    private bool _disposed;

    public LyricStyleRouter(
        IEnumerable<IKineticStyleRenderer>? renderers = null,
        PlaybackLifecycleChoreographer? lifecycle = null,
        StageBackdropRenderer? backdrop = null,
        AudioVisualizerRenderer? visualizer = null,
        EdgeGlowRenderer? edgeGlow = null)
    {
        _lifecycle = lifecycle ?? new PlaybackLifecycleChoreographer();
        _backdrop = backdrop ?? new StageBackdropRenderer();
        _visualizer = visualizer ?? new AudioVisualizerRenderer();
        _edgeGlow = edgeGlow ?? new EdgeGlowRenderer();
        _styles = new Dictionary<OverlayStyleId, IKineticStyleRenderer>();

        if (renderers is not null)
        {
            foreach (var r in renderers)
            {
                _styles[r.StyleId] = r;
            }
        }

        if (_styles.Count == 0)
        {
            _styles[OverlayStyleId.Fisheye] = new FisheyeStyleRenderer();
            _styles[OverlayStyleId.FisheyeVisual] = new FisheyeVisualStyleRenderer();
            _styles[OverlayStyleId.Codestyle] = new CodestyleStyleRenderer();
            _styles[OverlayStyleId.CodestyleVisual] = new CodestyleVisualStyleRenderer();
            _styles[OverlayStyleId.VisualStack] = new VisualStackStyleRenderer();
            _styles[OverlayStyleId.Constellation] = new ConstellationStyleRenderer();
            _styles[OverlayStyleId.SpidersWeb] = new SpidersWebStyleRenderer();
            _styles[OverlayStyleId.NodeLine] = new NodeLineStyleRenderer();
            _styles[OverlayStyleId.KineticWall] = new KineticWallStyleRenderer();
            _styles[OverlayStyleId.Messages] = new MessagesStyleRenderer();
            _styles[OverlayStyleId.VinylGroove] = new VinylGrooveStyleRenderer();
        }

        _fallbackStyle = _styles.TryGetValue(OverlayStyleId.Fisheye, out var fisheye)
            ? fisheye
            : _styles.Values.First();
    }

    internal StageBackdropRenderer BackdropRenderer => _backdrop;

    public OverlayStyleId StyleId => _lastRoutedStyle;

    public LifecycleFrameState LastLifecycleState
    {
        get
        {
            lock (_sync)
            {
                return _lastLifecycle;
            }
        }
    }

    public IReadOnlyCollection<OverlayStyleId> RegisteredStyles => _styles.Keys;

    /// <summary>
    /// Returns the canonical staggered Neon Horizon phase offset (in seconds) for a Gallery live preview card
    /// so all 11 style cards display their signature lyrics asynchronously per MOTION_AND_STYLES.md section 7.3.
    /// </summary>
    public static double GetGalleryPreviewPhaseOffsetSeconds(OverlayStyleId styleId) =>
        StyleLayoutHelper.GetGalleryPreviewPhaseOffsetSeconds(styleId);

    public IKineticStyleRenderer ResolveRenderer(OverlayStyleId requestedStyle) =>
        _styles.TryGetValue(requestedStyle, out var renderer) ? renderer : _fallbackStyle;

    public bool AdvancePhysics(in FrameRenderPacket packet)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var effectiveStyle = packet.EffectiveStyle;
            var renderer = ResolveRenderer(effectiveStyle);
            var styleChanged = effectiveStyle != _lastRoutedStyle;
            var trackGen = packet.PlaybackSnapshot?.TrackGeneration ?? -1;
            var seekGen = packet.PlaybackSnapshot?.SeekGeneration ?? -1;
            var offsetGen = packet.PlaybackSnapshot?.OffsetGeneration ?? -1;

            var trackChanged = trackGen != _lastTrackGeneration;
            var offsetNudged = _lastOffsetGeneration >= 0 && offsetGen > _lastOffsetGeneration;
            var hardSeek = _lastSeekGeneration >= 0 &&
                           seekGen != _lastSeekGeneration &&
                           !offsetNudged;

            _lastRoutedStyle = effectiveStyle;
            _lastTrackGeneration = trackGen;
            _lastSeekGeneration = seekGen;
            _lastOffsetGeneration = offsetGen;
            _lastEffectivePos = packet.SceneSnapshot.EffectivePosition;

            // Soft-retarget springs on offset nudges; hard-reset only on style/track switch or manual seek
            if (styleChanged || trackChanged || hardSeek)
            {
                renderer.ResetState();
            }

            if (trackChanged)
            {
                _backdrop.Reset();
            }

            var isNeonRushStage = IsNeonRushStage(in packet);
            var backdropActive = !isNeonRushStage && _backdrop.Advance(in packet);
            _lastLifecycle = _lifecycle.Advance(in packet);
            var styleActive = _lastLifecycle.ShouldRenderLyrics && renderer.Advance(in packet, in _lastLifecycle);
            return backdropActive || _lastLifecycle.IsAnimating || styleActive;
        }
    }

    private static bool IsNeonRushStage(in FrameRenderPacket packet) =>
        packet.ShouldDrawStageBackdrop
        && packet.SurfaceMode == StageRenderSurfaceMode.StageCanvas
        && packet.EffectiveStyle != OverlayStyleId.VinylGroove
        && (packet.PlaybackSnapshot?.Settings.EdgeGlowMode ?? AppSettings.Default.EdgeGlowMode) == EdgeGlowMode.NeonRush;

    public void Render(in StyleRenderContext context)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var canvas = context.Canvas;
            var packet = context.Packet;
            var isNeonRushStage = IsNeonRushStage(in packet);
            if (!packet.ShouldDrawStageBackdrop)
            {
                canvas.Clear(SKColors.Transparent);
            }
            else if (isNeonRushStage)
            {
                // Pitch-black OLED canvas so only Neon Rush racing beams illuminate the perimeter
                canvas.Clear(SKColors.Black);
            }

            var lifecycle = _lastLifecycle;

            // Stage Canvas / Gallery Card backdrop (skipped on transparent DesktopOverlay & pitch-black Neon Rush stage)
            var resolvedStyle = ResolveRenderer(packet.EffectiveStyle);
            var vinylStageMode = packet.ShouldDrawStageBackdrop &&
                                 !isNeonRushStage &&
                                 resolvedStyle is VinylGrooveStyleRenderer;

            if (packet.ShouldDrawStageBackdrop && !isNeonRushStage)
            {
                if (resolvedStyle is VinylGrooveStyleRenderer vinylBackdrop)
                {
                    vinylBackdrop.RenderStageRoomBackdrop(in context);
                }
                else
                {
                    _backdrop.Render(in context);
                }
            }

            // Layer 0.5: Audio-reactive background visualizer & perimeter Edges Glow
            _visualizer.Render(in context);
            _edgeGlow.Render(in context);

            if (vinylStageMode && resolvedStyle is VinylGrooveStyleRenderer vinylForeground)
            {
                var opacitySave = context.Legibility.BeginGlobalOpacityLayer(canvas, Math.Clamp(packet.GlobalOpacity, 0f, 1f));
                try
                {
                    vinylForeground.RenderStageForeground(in context, in lifecycle);
                }
                finally
                {
                    LegibilityPipeline.EndGlobalOpacityLayer(canvas, opacitySave);
                }
            }
            else if (lifecycle.ShouldRenderLyrics)
            {
                var effectiveOpacity = Math.Clamp(packet.GlobalOpacity * lifecycle.ContentOpacity, 0f, 1f);
                if (effectiveOpacity > 0.004f)
                {
                    var opacitySave = context.Legibility.BeginGlobalOpacityLayer(canvas, effectiveOpacity);
                    try
                    {
                        resolvedStyle.Render(in context, in lifecycle);
                    }
                    finally
                    {
                        LegibilityPipeline.EndGlobalOpacityLayer(canvas, opacitySave);
                    }
                }
            }

            if (lifecycle.ShowInstrumentalIndicator)
            {
                _lifecycle.DrawInstrumentalIndicator(in context, in lifecycle);
            }

            if (lifecycle.ShouldRenderToast)
            {
                _lifecycle.DrawOffsetToast(in context, in lifecycle);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var renderer in _styles.Values.Distinct())
            {
                renderer.Dispose();
            }

            _styles.Clear();
            _visualizer.Dispose();
            _edgeGlow.Dispose();
            _backdrop.Dispose();
            _lifecycle.Dispose();
        }
    }
}
