using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LyricPulse.Core.Lyrics;
using LyricPulse.Core.Models;
using LyricPulse.Core.Runtime;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;

namespace LyricPulse.UI.Controls;

/// <summary>
/// Reusable Avalonia control wrapping <see cref="LyricCanvasControl"/> for both the full-bleed
/// Hero <see cref="StageRenderSurfaceMode.StageCanvas"/> and each of the 10 compact
/// <see cref="StageRenderSurfaceMode.GalleryCard"/> thumbnails (with 30/60 FPS throttling & phase staggering).
/// </summary>
public sealed class StageCardCanvasHost : ContentControl, IDisposable
{
    public static readonly StyledProperty<StageRenderSurfaceMode> SurfaceModeProperty =
        AvaloniaProperty.Register<StageCardCanvasHost, StageRenderSurfaceMode>(nameof(SurfaceMode), StageRenderSurfaceMode.StageCanvas);

    public static readonly StyledProperty<OverlayStyleId?> StyleOverrideProperty =
        AvaloniaProperty.Register<StageCardCanvasHost, OverlayStyleId?>(nameof(StyleOverride));

    public static readonly StyledProperty<double> PreviewPhaseSecondsProperty =
        AvaloniaProperty.Register<StageCardCanvasHost, double>(nameof(PreviewPhaseSeconds), 0.0);

    public static readonly StyledProperty<bool> IsCardPausedProperty =
        AvaloniaProperty.Register<StageCardCanvasHost, bool>(nameof(IsCardPaused), false);

    public static readonly StyledProperty<int> TargetMaxFpsProperty =
        AvaloniaProperty.Register<StageCardCanvasHost, int>(nameof(TargetMaxFps), 0);

    public static readonly StyledProperty<StageBackdropMode?> StageBackdropOverrideProperty =
        AvaloniaProperty.Register<StageCardCanvasHost, StageBackdropMode?>(nameof(StageBackdropOverride));

    public static readonly StyledProperty<bool> BottomFadeToBlackProperty =
        AvaloniaProperty.Register<StageCardCanvasHost, bool>(nameof(BottomFadeToBlack), false);

    private static LyricsDocument? s_fallbackDemoDoc;
    private static readonly Lock s_demoLock = new();

    private readonly OverlayRuntimeController _runtime;
    private readonly LyricCanvasControl _canvas;
    private bool _disposed;

    public StageCardCanvasHost(
        OverlayRuntimeController runtime,
        FontAssetRegistry fonts,
        ShaderLibrary shaders,
        FrameBudgetTelemetry telemetry,
        ILyricSceneRendererFactory rendererFactory)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(rendererFactory);

        _runtime = runtime;
        var renderer = rendererFactory.CreateRenderer();

        _canvas = new LyricCanvasControl(
            fonts,
            shaders,
            telemetry,
            renderer,
            packetFactory: BuildPacket,
            ownsRenderer: true);

        Content = _canvas;
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    public StageRenderSurfaceMode SurfaceMode
    {
        get => GetValue(SurfaceModeProperty);
        set => SetValue(SurfaceModeProperty, value);
    }

    public OverlayStyleId? StyleOverride
    {
        get => GetValue(StyleOverrideProperty);
        set => SetValue(StyleOverrideProperty, value);
    }

    public double PreviewPhaseSeconds
    {
        get => GetValue(PreviewPhaseSecondsProperty);
        set => SetValue(PreviewPhaseSecondsProperty, value);
    }

    public bool IsCardPaused
    {
        get => GetValue(IsCardPausedProperty);
        set => SetValue(IsCardPausedProperty, value);
    }

    public int TargetMaxFps
    {
        get => GetValue(TargetMaxFpsProperty);
        set => SetValue(TargetMaxFpsProperty, value);
    }

    public StageBackdropMode? StageBackdropOverride
    {
        get => GetValue(StageBackdropOverrideProperty);
        set => SetValue(StageBackdropOverrideProperty, value);
    }

    public bool BottomFadeToBlack
    {
        get => GetValue(BottomFadeToBlackProperty);
        set => SetValue(BottomFadeToBlackProperty, value);
    }

    public void RequestWake() => _canvas.RequestWake();

    public void ReleaseGpuResources() => _canvas.ReleaseGpuResources();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _canvas.Dispose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCardPausedProperty)
        {
            _canvas.IsPaused = IsCardPaused;
        }
        else if (change.Property == TargetMaxFpsProperty)
        {
            _canvas.TargetMaxFps = TargetMaxFps;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _canvas.IsPaused = true;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _canvas.IsPaused = IsCardPaused;
        _canvas.TargetMaxFps = TargetMaxFps;
        _canvas.RequestWake();
    }

    private FrameRenderPacket BuildPacket(float w, float h, float scale, double timeSec, float dt)
    {
        var snap = _runtime.SampleFrame();
        var mode = SurfaceMode;

        // When idle with no live media session, StageCanvas and GalleryCard animate the built-in
        // Neon Horizon demo song cleanly so the studio never looks dead on first launch!
        if (snap.Document is null || snap.LyricsStatus != LyricsStatus.Synced)
        {
            var demoDoc = GetFallbackDemoDocument();
            var phaseSec = (timeSec + PreviewPhaseSeconds) % DemoSongProvider.LoopDuration.TotalSeconds;
            var demoPos = TimeSpan.FromSeconds(phaseSec);
            snap = snap with
            {
                Track = demoDoc.Track,
                Document = demoDoc,
                LyricsStatus = LyricsStatus.Synced,
                PlaybackState = MediaPlaybackState.Playing,
                RawPosition = demoPos,
                EffectivePosition = demoPos,
                ActiveSlice = ActiveLyricSlice.Resolve(demoDoc, demoPos, DemoSongProvider.LoopDuration),
                ActivePalette = snap.Settings.AutoSyncColors
                    ? DemoSongProvider.DemoPalette
                    : snap.ActivePalette,
                IsPlaying = true,
                Settings = snap.Settings with { LyricsVisible = true }
            };
        }
        else if (mode == StageRenderSurfaceMode.GalleryCard && PreviewPhaseSeconds > 0.01 && snap.Document is { } liveDoc)
        {
            // Stagger phase across the 10 live cards when previewing
            var durationSec = Math.Max(liveDoc.Track.Duration.TotalSeconds, 36.0);
            var staggeredSec = (snap.EffectivePosition.TotalSeconds + PreviewPhaseSeconds) % durationSec;
            var pos = TimeSpan.FromSeconds(staggeredSec);
            snap = snap with
            {
                EffectivePosition = pos,
                ActiveSlice = ActiveLyricSlice.Resolve(liveDoc, pos, liveDoc.Track.Duration)
            };
        }

        return OverlayFrameAdapter.FromPlaybackSnapshot(
            snap,
            viewportWidthDip: w,
            viewportHeightDip: h,
            renderScaling: scale,
            renderTimeSeconds: timeSec,
            deltaSeconds: dt,
            surfaceMode: mode,
            stageBackdropOverride: StageBackdropOverride,
            styleOverride: StyleOverride,
            bottomFadeToBlack: BottomFadeToBlack);
    }

    private static LyricsDocument GetFallbackDemoDocument()
    {
        if (s_fallbackDemoDoc is not null)
        {
            return s_fallbackDemoDoc;
        }

        lock (s_demoLock)
        {
            if (s_fallbackDemoDoc is not null)
            {
                return s_fallbackDemoDoc;
            }

            var provider = new DemoSongProvider(new LyricsParser(), new TitleNormalizer());
            var doc = provider.GetDemoDocument();
            using var matcher = new Core.Visuals.GloveSemanticVisualMatcher();
            matcher.InitializeAsync().GetAwaiter().GetResult();
            s_fallbackDemoDoc = matcher.AnnotateDocument(doc, VisualPreference.Balanced);
            return s_fallbackDemoDoc;
        }
    }
}
