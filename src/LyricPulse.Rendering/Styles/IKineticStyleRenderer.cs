using LyricPulse.Core.Models;
using LyricPulse.Rendering.Host;

namespace LyricPulse.Rendering.Styles;

/// <summary>
/// Contract implemented by individual kinetic lyric style renderers managed by <see cref="LyricStyleRouter"/>.
/// </summary>
public interface IKineticStyleRenderer : IDisposable
{
    OverlayStyleId StyleId { get; }

    /// <summary>Resets internal springs and animation caches on style or track change.</summary>
    void ResetState();

    /// <summary>
    /// Advances per-style springs on the UI thread.
    /// Returns true while any spring or visual cadence requires another vsync frame.
    /// </summary>
    bool Advance(in FrameRenderPacket packet, in LifecycleFrameState lifecycle);

    /// <summary>Renders the style inside the lifecycle-attenuated canvas layer.</summary>
    void Render(in StyleRenderContext context, in LifecycleFrameState lifecycle);
}
