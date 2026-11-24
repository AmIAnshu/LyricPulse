using LyricPulse.Core.Models;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using SkiaSharp;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Authoritative Phase 3+ <see cref="ILyricSceneRenderer"/> coordinating
/// <see cref="PlaybackLifecycleChoreographer"/> with registered kinetic style renderers
/// (<c>Fisheye</c>, <c>Codestyle</c>, <c>VisualStack</c>, with graceful fallback to <c>Fisheye</c>
/// for Phase 4 styles) and automatic state reset on style/track/seek changes.
/// </summary>
public sealed class LyricStyleRouter : ILyricSceneRenderer, IDisposable
{
    private readonly Dictionary<OverlayStyleId, IKineticStyleRenderer> _styles;
    private readonly IKineticStyleRenderer _fallbackStyle;
    private readonly PlaybackLifecycleChoreographer _lifecycle;

    private OverlayStyleId _lastRoutedStyle = OverlayStyleId.Fisheye;
    private long _lastTrackGeneration = -1;
    private long _lastSeekGeneration = -1;
    private LifecycleFrameState _lastLifecycle;
    private bool _disposed;

    public LyricStyleRouter(
        IEnumerable<IKineticStyleRenderer>? renderers = null,
        PlaybackLifecycleChoreographer? lifecycle = null)
    {
        _lifecycle = lifecycle ?? new PlaybackLifecycleChoreographer();
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
            _styles[OverlayStyleId.Codestyle] = new CodestyleStyleRenderer();
            _styles[OverlayStyleId.VisualStack] = new VisualStackStyleRenderer();
        }

        _fallbackStyle = _styles.TryGetValue(OverlayStyleId.Fisheye, out var fisheye)
            ? fisheye
            : _styles.Values.First();
    }

    public OverlayStyleId StyleId => _lastRoutedStyle;

    public LifecycleFrameState LastLifecycleState => _lastLifecycle;

    public IReadOnlyCollection<OverlayStyleId> RegisteredStyles => _styles.Keys;

    public IKineticStyleRenderer ResolveRenderer(OverlayStyleId requestedStyle) =>
        _styles.TryGetValue(requestedStyle, out var renderer) ? renderer : _fallbackStyle;

    public bool AdvancePhysics(in FrameRenderPacket packet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var renderer = ResolveRenderer(packet.ActiveStyle);
        var styleChanged = packet.ActiveStyle != _lastRoutedStyle;
        var trackGen = packet.PlaybackSnapshot?.TrackGeneration ?? -1;
        var seekGen = packet.PlaybackSnapshot?.SeekGeneration ?? -1;

        if (styleChanged || trackGen != _lastTrackGeneration || seekGen != _lastSeekGeneration)
        {
            _lastRoutedStyle = packet.ActiveStyle;
            _lastTrackGeneration = trackGen;
            _lastSeekGeneration = seekGen;
            renderer.ResetState();
        }

        _lastLifecycle = _lifecycle.Advance(in packet);
        var styleActive = _lastLifecycle.ShouldRenderLyrics && renderer.Advance(in packet, in _lastLifecycle);
        return _lastLifecycle.IsAnimating || styleActive;
    }

    public void Render(in StyleRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var canvas = context.Canvas;
        canvas.Clear(SKColors.Transparent);

        var lifecycle = _lastLifecycle;
        var packet = context.Packet;

        if (lifecycle.ShouldRenderLyrics)
        {
            var effectiveOpacity = Math.Clamp(packet.GlobalOpacity * lifecycle.ContentOpacity, 0f, 1f);
            if (effectiveOpacity > 0.004f)
            {
                var opacitySave = context.Legibility.BeginGlobalOpacityLayer(canvas, effectiveOpacity);
                try
                {
                    var renderer = ResolveRenderer(packet.ActiveStyle);
                    renderer.Render(in context, in lifecycle);
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

    public void Dispose()
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
        _lifecycle.Dispose();
    }
}
