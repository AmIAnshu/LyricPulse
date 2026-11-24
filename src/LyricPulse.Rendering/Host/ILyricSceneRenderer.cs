using LyricPulse.Core.Models;

namespace LyricPulse.Rendering.Host;

/// <summary>
/// Contract implemented by every kinetic lyric style renderer (and the Phase 2 diagnostic renderer).
/// </summary>
public interface ILyricSceneRenderer
{
    OverlayStyleId StyleId { get; }

    /// <summary>
    /// Advances spring physics on the UI thread before rendering.
    /// Returns true while any animation is active; returning false allows the RAF loop to sleep at &lt;1% CPU.
    /// </summary>
    bool AdvancePhysics(in FrameRenderPacket packet);

    /// <summary>
    /// Renders the immutable frame snapshot onto the leased SkiaSharp canvas on the render thread.
    /// </summary>
    void Render(in StyleRenderContext context);
}
