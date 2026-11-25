using System.Windows.Input;
using LyricPulse.Core.Models;

namespace LyricPulse.UI.ViewModels;

public sealed class StyleCardViewModel : ViewModelBase
{
    private bool _isActive;
    private bool _isPinned;
    private bool _isHovered;
    private bool _isVisibleInStrip = true;

    public StyleCardViewModel(
        StyleMetadata metadata,
        ICommand selectCommand,
        ICommand togglePinCommand)
    {
        Metadata = metadata;
        SelectCommand = selectCommand;
        TogglePinCommand = togglePinCommand;
    }

    public StyleMetadata Metadata { get; }
    public OverlayStyleId StyleId => Metadata.StyleId;
    public string DisplayName => Metadata.DisplayName;
    public string CapabilityChip => Metadata.CapabilityChip;
    public string Description => Metadata.Description;
    public double PhaseOffsetSeconds => Metadata.PreviewPhaseSeconds;
    public bool HasVisualIcons => Metadata.HasVisualIcons;

    public ICommand SelectCommand { get; }
    public ICommand TogglePinCommand { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetProperty(ref _isPinned, value))
            {
                OnPropertyChanged(nameof(PinToolTip));
            }
        }
    }

    public bool IsHovered
    {
        get => _isHovered;
        set => SetProperty(ref _isHovered, value);
    }

    public bool IsVisibleInStrip
    {
        get => _isVisibleInStrip;
        set => SetProperty(ref _isVisibleInStrip, value);
    }

    public string PinToolTip => IsPinned
        ? $"Unpin {DisplayName} from System Tray"
        : $"Pin {DisplayName} to System Tray";
}
