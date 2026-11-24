using Avalonia;
using Avalonia.Skia;
using Avalonia.Win32;
using LyricPulse.App.Platform;
using LyricPulse.Rendering.Fonts;

namespace LyricPulse.App;

public static class Program
{
    public const long MaxSkiaGpuResourceBytes = 32L * 1024L * 1024L;

    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstance = SingleInstanceGuard.TryAcquirePrimaryOrSignalExisting();
        if (singleInstance is null)
        {
            return;
        }

        App.ActiveSingleInstanceGuard = singleInstance;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            App.ActiveSingleInstanceGuard = null;
        }
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
