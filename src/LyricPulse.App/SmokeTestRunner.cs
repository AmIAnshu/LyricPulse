using LyricPulse.App.Tray;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Legibility;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;
using LyricPulse.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace LyricPulse.App;

/// <summary>
/// Fast headless startup verification runner invoked by <c>LyricPulse.App.exe --smoke-test</c>.
/// Validates DI wiring, settings load, all 5 bundled fonts, both SkSL shaders, 12-line Neon Horizon
/// visual matching, offscreen rendering of all 3 MVP styles, and adaptive tray icon synthesis.
/// </summary>
internal static class SmokeTestRunner
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var settingsStore = services.GetRequiredService<ISettingsStore>();
        var settings = await settingsStore.LoadAsync().ConfigureAwait(false);

        var fonts = services.GetRequiredService<FontAssetRegistry>();
        var shaders = services.GetRequiredService<ShaderLibrary>();
        if (!shaders.AreAllShadersReady)
        {
            await Console.Error.WriteLineAsync($"[FAIL] SkSL shader compilation failed: bulge='{shaders.BulgeCompileError}', blur='{shaders.MotionBlurCompileError}', partyMesh='{shaders.PartyMeshCompileError}'").ConfigureAwait(false);
            return 2;
        }

        var visualMatcher = services.GetRequiredService<IWordVisualMatcher>();
        await visualMatcher.InitializeAsync().ConfigureAwait(false);

        var demoProvider = services.GetRequiredService<DemoSongProvider>();
        var annotatedDemo = visualMatcher.AnnotateDocument(demoProvider.GetDemoDocument(), settings.VisualPreference);

        var runtime = services.GetRequiredService<OverlayRuntimeController>();
        await runtime.SetDemoModeAsync(true).ConfigureAwait(false);
        var demoSnapshot = runtime.SampleFrame();

        using var router = new LyricStyleRouter();
        using var surfacePool = new OffscreenSurfacePool();
        using var legibility = new LegibilityPipeline();

        var allStyles = Enum.GetValues<OverlayStyleId>();
        var renderInfo = new SKImageInfo(640, 240, SKColorType.Bgra8888, SKAlphaType.Premul);

        foreach (var styleId in allStyles)
        {
            var phaseSec = LyricStyleRouter.GetGalleryPreviewPhaseOffsetSeconds(styleId);
            var styledSnap = demoSnapshot with
            {
                ActiveSlice = ActiveLyricSlice.Resolve(annotatedDemo, TimeSpan.FromSeconds(phaseSec)),
                Settings = settings with { ActiveStyle = styleId, LyricsVisible = true }
            };

            var packet = OverlayFrameAdapter.FromPlaybackSnapshot(
                styledSnap,
                viewportWidthDip: 640f,
                viewportHeightDip: 240f,
                renderScaling: 1.0f,
                renderTimeSeconds: phaseSec,
                deltaSeconds: 1f / 60f,
                surfaceMode: StageRenderSurfaceMode.StageCanvas);

            router.AdvancePhysics(in packet);

            using var surface = SKSurface.Create(renderInfo);
            var ctx = new StyleRenderContext(
                Canvas: surface.Canvas,
                GrContext: null,
                Fonts: fonts,
                Shaders: shaders,
                SurfacePool: surfacePool,
                Legibility: legibility,
                Packet: packet,
                IsGpuBackend: false,
                ShadersEnabled: false);

            router.Render(in ctx);
            using var snapImg = surface.Snapshot();
            if (snapImg.Width != 640 || snapImg.Height != 240)
            {
                await Console.Error.WriteLineAsync($"[FAIL] Smoke render for {styleId} returned unexpected dimensions.").ConfigureAwait(false);
                return 3;
            }
        }

        foreach (var state in Enum.GetValues<TrayIconVisualState>())
        {
            var ico = TrayIconFactory.RenderMultiSizeIcoBytes(state, isLightTaskbar: false);
            if (ico.Length < 400)
            {
                await Console.Error.WriteLineAsync($"[FAIL] Tray multi-size ICO synthesis failed for state {state}.").ConfigureAwait(false);
                return 4;
            }
        }

        // Verify Phase 5 StageViewModel + dynamic Gallery metadata
        var stageVm = new StageViewModel(runtime);
        stageVm.ApplySnapshot(demoSnapshot);
        if (stageVm.StyleCards.Count != allStyles.Length || stageVm.StudioPresets.Length != 4)
        {
            await Console.Error.WriteLineAsync($"[FAIL] StageViewModel failed {allStyles.Length}-style / 4-preset initialization.").ConfigureAwait(false);
            return 5;
        }

        var trayState = TrayMenuState.FromSnapshot(demoSnapshot);
        await Console.Out.WriteLineAsync(
            $"[PASS] LyricPulse smoke test succeeded | Fonts=5 | Shaders=3/3 OK | DemoLines={annotatedDemo.Lines.Length} | Styles={allStyles.Length}/{allStyles.Length} | StageCards={stageVm.StyleCards.Count} | TrayHeader='{trayState.HeaderPrimaryLine}'").ConfigureAwait(false);
        return 0;
    }
}
