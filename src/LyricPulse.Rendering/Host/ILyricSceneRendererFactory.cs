using LyricPulse.Rendering.Styles;

namespace LyricPulse.Rendering.Host;

/// <summary>
/// Non-root-DI factory constructing per-overlay <see cref="ILyricSceneRenderer"/> instances
/// whose lifecycle and disposal are owned directly by <see cref="LyricCanvasControl"/>
/// (preventing MS.DI root container retention of transient IDisposable Skia renderers).
/// </summary>
public interface ILyricSceneRendererFactory
{
    ILyricSceneRenderer CreateRenderer();
}

public sealed class LyricSceneRendererFactory : ILyricSceneRendererFactory
{
    public ILyricSceneRenderer CreateRenderer() => new LyricStyleRouter();
}
