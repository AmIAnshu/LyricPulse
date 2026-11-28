using System.Net.Security;
using System.Security.Authentication;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Media;
using LyricPulse.Core.Runtime;
using LyricPulse.Core.Settings;
using LyricPulse.Core.Visuals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LyricPulse.Core;

/// <summary>
/// Dependency injection registration extensions for LyricPulse.Core services.
/// Uses named IHttpClientFactory clients inside singleton providers to avoid captive transient HttpClients.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLyricPulseCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITitleNormalizer, TitleNormalizer>();
        services.AddSingleton<ILyricsParser, LyricsParser>();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore());
        services.AddSingleton<ITrackOffsetStore>(_ => new TrackOffsetStore());
        services.AddSingleton<TimingOffsetService>();
        services.AddSingleton<DemoSongProvider>();
        services.AddSingleton<IWordVisualMatcher>(sp =>
            new GloveSemanticVisualMatcher(logger: sp.GetService<ILogger<GloveSemanticVisualMatcher>>()));
        services.AddSingleton<ILyricsCache>(sp =>
            new DiskLyricsCache(cacheDirectory: null, sp.GetRequiredService<ILyricsParser>()));
        services.AddSingleton(sp =>
            new LocalLrcSidecarProvider(sidecarDirectory: null, sp.GetRequiredService<ILyricsParser>()));

        services.AddHttpClient(LrcLibLyricsProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://lrclib.net");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(CreateHardenedHttpHandler);

        services.AddHttpClient(NetEaseLyricsProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://music.163.com");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(CreateHardenedHttpHandler);

        services.AddSingleton(sp => new LrcLibLyricsProvider(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILyricsParser>(),
            sp.GetRequiredService<ITitleNormalizer>(),
            sp.GetRequiredService<ILogger<LrcLibLyricsProvider>>()));
        services.AddSingleton(sp => new NetEaseLyricsProvider(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILyricsParser>(),
            sp.GetRequiredService<ITitleNormalizer>(),
            sp.GetRequiredService<ILogger<NetEaseLyricsProvider>>()));

        services.AddSingleton<ILyricsProvider>(sp => sp.GetRequiredService<LrcLibLyricsProvider>());
        services.AddSingleton<ILyricsProvider>(sp => sp.GetRequiredService<NetEaseLyricsProvider>());
        services.AddSingleton<ILyricsOrchestrator, LyricsOrchestrator>();
        services.AddSingleton<IMediaSessionProvider, WindowsGsmtcSessionProvider>();
        services.AddSingleton(sp => new OverlayRuntimeController(
            sp.GetRequiredService<IMediaSessionProvider>(),
            sp.GetRequiredService<ILyricsOrchestrator>(),
            sp.GetRequiredService<IWordVisualMatcher>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<TimingOffsetService>(),
            sp.GetRequiredService<DemoSongProvider>(),
            sp.GetService<IAlbumColorExtractor>(),
            sp.GetService<IFullscreenDetector>(),
            sp.GetService<ILogger<OverlayRuntimeController>>()));

        return services;
    }

    internal static SocketsHttpHandler CreateHardenedHttpHandler() =>
        new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            MaxResponseHeadersLength = 16,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }
        };
}
