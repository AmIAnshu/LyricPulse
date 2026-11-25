using Avalonia.Controls;
using Avalonia.Media;
using LyricPulse.Rendering.Fonts;
using LyricPulse.Rendering.Host;
using LyricPulse.Rendering.Shaders;
using LyricPulse.Rendering.Styles;
using LyricPulse.UI.Controls;
using LyricPulse.UI.ViewModels;

namespace LyricPulse.UI.Views;

public partial class ObsBroadcastWindow : Window, IDisposable
{
    private readonly StageCardCanvasHost? _canvasHost;
    private bool _disposed;

    public ObsBroadcastWindow()
    {
        InitializeComponent();
    }

    public ObsBroadcastWindow(
        StageViewModel viewModel,
        FontAssetRegistry fonts,
        ShaderLibrary shaders,
        FrameBudgetTelemetry telemetry,
        ILyricSceneRendererFactory rendererFactory)
    {
        InitializeComponent();
        DataContext = viewModel;

        _canvasHost = new StageCardCanvasHost(viewModel.Runtime, fonts, shaders, telemetry, rendererFactory)
        {
            SurfaceMode = StageRenderSurfaceMode.ObsBroadcast
        };
        CanvasHostSlot.Content = _canvasHost;

        GreenKeyBtn.Click += (_, _) => SetChroma("#00FF00", ObsChromaKeyMode.Green, viewModel);
        BlueKeyBtn.Click += (_, _) => SetChroma("#0000FF", ObsChromaKeyMode.Blue, viewModel);
        BlackKeyBtn.Click += (_, _) => SetChroma("#000000", ObsChromaKeyMode.Black, viewModel);
        CloseBtn.Click += (_, _) => Close();
    }

    private void SetChroma(string hex, ObsChromaKeyMode mode, StageViewModel vm)
    {
        vm.ObsChromaMode = mode;
        ChromaBackdrop.Background = SolidColorBrush.Parse(hex);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _canvasHost?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is StageViewModel vm)
        {
            vm.IsObsWindowOpen = false;
        }

        Dispose();
    }
}
