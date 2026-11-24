using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Media;
using LyricPulse.Core.Settings;
using LyricPulse.Core.Visuals;
using Microsoft.Extensions.DependencyInjection;

namespace LyricPulse.Core;

/// <summary>
/// Dependency injection registration extensions for LyricPulse.Core services.
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
        services.AddSingleton<IWordVisualMatcher, StarterLexiconVisualMatcher>();
        services.AddSingleton<ILyricsCache>(sp =>
            new DiskLyricsCache(cacheDirectory: null, sp.GetRequiredService<ILyricsParser>()));
        services.AddSingleton(sp =>
            new LocalLrcSidecarProvider(sidecarDirectory: null, sp.GetRequiredService<ILyricsParser>()));

        services.AddHttpClient<LrcLibLyricsProvider>(client =>
        {
            client.BaseAddress = new Uri("https://lrclib.net");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddHttpClient<NetEaseLyricsProvider>(client =>
        {
            client.BaseAddress = new Uri("https://music.163.com");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddSingleton<ILyricsProvider>(sp => sp.GetRequiredService<LrcLibLyricsProvider>());
        services.AddSingleton<ILyricsProvider>(sp => sp.GetRequiredService<NetEaseLyricsProvider>());
        services.AddSingleton<ILyricsOrchestrator, LyricsOrchestrator>();
        services.AddSingleton<IMediaSessionProvider, WindowsGsmtcSessionProvider>();

        return services;
    }
}
