using System.Windows.Input;
using LyricPulse.Core.Models;

namespace LyricPulse.UI.ViewModels;

public sealed record DisplayMonitorInfo(
    string DisplayKey,
    string FriendlyName,
    int WorkingWidthPx,
    int WorkingHeightPx,
    double EffectiveDpiScale,
    bool IsPrimary);

public sealed class DisplayOptionViewModel : ViewModelBase
{
    private bool _isEnabled;

    public DisplayOptionViewModel(
        int index,
        string displayKey,
        string friendlyName,
        int widthPx,
        int heightPx,
        double dpiScale,
        bool isPrimary,
        bool isEnabled,
        Action<string, bool> onToggle)
    {
        Index = index;
        DisplayKey = displayKey;
        FriendlyName = friendlyName;
        WidthPx = widthPx;
        HeightPx = heightPx;
        DpiScale = dpiScale;
        IsPrimary = isPrimary;
        _isEnabled = isEnabled;
        _onToggle = onToggle;
    }

    private readonly Action<string, bool> _onToggle;

    public int Index { get; }
    public string DisplayKey { get; }
    public string FriendlyName { get; }
    public int WidthPx { get; }
    public int HeightPx { get; }
    public double DpiScale { get; }
    public bool IsPrimary { get; }

    public string HeaderText => IsPrimary
        ? $"Display {Index} · Primary"
        : $"Display {Index} · {FriendlyName}";

    public string TelemetryText => $"{WidthPx} × {HeightPx} · {(int)Math.Round(DpiScale * 100)}% DPI";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _onToggle(DisplayKey, value);
            }
        }
    }
}

public sealed class HotkeyBindingViewModel : ViewModelBase
{
    private string _gestureText;
    private bool _isRecording;
    private string? _errorMessage;

    public HotkeyBindingViewModel(
        HotkeyAction action,
        string label,
        string initialGesture,
        ICommand beginRecordCommand,
        ICommand clearCommand)
    {
        Action = action;
        Label = label;
        _gestureText = initialGesture;
        BeginRecordCommand = beginRecordCommand;
        ClearCommand = clearCommand;
    }

    public HotkeyAction Action { get; }
    public string Label { get; }
    public ICommand BeginRecordCommand { get; }
    public ICommand ClearCommand { get; }

    public string GestureText
    {
        get => _gestureText;
        set
        {
            if (SetProperty(ref _gestureText, value))
            {
                OnPropertyChanged(nameof(DisplayButtonText));
            }
        }
    }

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (SetProperty(ref _isRecording, value))
            {
                OnPropertyChanged(nameof(DisplayButtonText));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    public string DisplayButtonText => IsRecording
        ? "Press keys... (Esc)"
        : string.IsNullOrWhiteSpace(_gestureText) ? "Unassigned" : _gestureText;
}
