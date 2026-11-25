using LyricPulse.Core.Abstractions;
using LyricPulse.Rendering.Color;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;
using Microsoft.Extensions.DependencyInjection;

namespace LyricPulse.Rendering;

/// <summary>
/// Dependency injection registration extensions for LyricPulse.Rendering services.
/// Uses <see cref="ILyricSceneRendererFactory"/> for per-overlay renderers so root ServiceProvider
/// never retains transient IDisposable Skia resource graphs.
/// </summary>
public static class RenderingServiceCollectionExtensions
{
    public static IServiceCollection AddLyricPulseRendering(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<FontAssetRegistry>();
        services.AddSingleton<ShaderLibrary>();
        services.AddSingleton<FrameBudgetTelemetry>();
        services.AddSingleton<IAlbumColorExtractor, SkiaAlbumColorExtractor>();
        services.AddSingleton<ILyricSceneRendererFactory, LyricSceneRendererFactory>();
        services.AddTransient<OffscreenSurfacePool>();
        services.AddTransient<LegibilityPipeline>();
        services.AddTransient<ILyricSceneRenderer>(static sp => sp.GetRequiredService<ILyricSceneRendererFactory>().CreateRenderer());

        return services;
    }
}
