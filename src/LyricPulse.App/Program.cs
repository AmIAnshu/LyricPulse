using Avalonia;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.Win32;
using LyricPulse.App.Platform;
using LyricPulse.App.Tray;
using LyricPulse.Core;
using LyricPulse.Core.Abstractions;
using LyricPulse.Rendering;
using LyricPulse.Rendering.Fonts;
using Microsoft.Extensions.DependencyInjection;

namespace LyricPulse.App;

public static class Program
{
    public const long MaxSkiaGpuResourceBytes = 32L * 1024L * 1024L;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && string.Equals(args[0], "--export-icon", StringComparison.OrdinalIgnoreCase))
        {
            var outPath = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllBytes(outPath, TrayIconFactory.RenderMultiSizeIcoBytes(TrayIconVisualState.Playing, isLightTaskbar: false));
            return 0;
        }

        var launchOptions = LaunchOptions.Parse(args);
        if (launchOptions.RunSmokeTest)
        {
            var smokeServices = BuildServiceProvider(forHeadlessSmokeTest: true);
            try
            {
                return SmokeTestRunner.RunAsync(smokeServices).GetAwaiter().GetResult();
            }
            finally
            {
                smokeServices.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        using var singleInstance = SingleInstanceGuard.TryAcquirePrimaryOrSignalExisting(
            requestDemoMode: launchOptions.StartInDemoMode);
        if (singleInstance is null)
        {
            return 0;
        }

        App.ActiveSingleInstanceGuard = singleInstance;
        App.LaunchOptions = launchOptions;

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            App.ActiveSingleInstanceGuard = null;
        }
    }

    public static ServiceProvider BuildServiceProvider(bool forHeadlessSmokeTest = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOverlayWindowPlatform, Win32OverlayInterop>();
        services.AddSingleton<IFullscreenDetector, FullscreenAutoHideMonitor>();
        services.AddSingleton<TrayIconFactory>();

        if (!forHeadlessSmokeTest)
        {
            services.AddSingleton<IGlobalHotkeyService>(_ =>
                new GlobalHotkeyManager(action => Dispatcher.UIThread.Post(action)));
        }

        services.AddLyricPulseCore();
        services.AddLyricPulseRendering();
        return services.BuildServiceProvider();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
            .WithInterFont()
            .ConfigureFonts(fontManager =>
            {
                fontManager.AddFontCollection(new LyricPulseFontCollection());
            })
            .With(new Win32PlatformOptions
            {
                RenderingMode =
                [
                    Win32RenderingMode.AngleEgl,
                    Win32RenderingMode.Software
                ],
                CompositionMode =
                [
                    Win32CompositionMode.WinUIComposition,
                    Win32CompositionMode.DirectComposition,
                    Win32CompositionMode.RedirectionSurface
                ],
                WinUICompositionBackdropCornerRadius = 0f,
                OverlayPopups = false
            })
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = MaxSkiaGpuResourceBytes
            })
            .LogToTrace();
}
