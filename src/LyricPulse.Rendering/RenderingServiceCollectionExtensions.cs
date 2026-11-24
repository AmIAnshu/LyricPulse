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
        services.AddTransient<OffscreenSurfacePool>();
        services.AddTransient<LegibilityPipeline>();
        services.AddTransient<PlaybackLifecycleChoreographer>();
        services.AddTransient<IKineticStyleRenderer, FisheyeStyleRenderer>();
        services.AddTransient<IKineticStyleRenderer, CodestyleStyleRenderer>();
        services.AddTransient<IKineticStyleRenderer, VisualStackStyleRenderer>();
        services.AddTransient<ILyricSceneRenderer, LyricStyleRouter>();

        return services;
    }
}
