using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Windows.Media.Control;

namespace LyricPulse.Core.Media;

/// <summary>
/// Manages WinRT event handler attachment and explicit COM-safe detachment
/// for a single tracked GSMTC session.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin WinRT COM event wrapper.")]
internal sealed class GsmtcSessionHandle(
    GlobalSystemMediaTransportControlsSession session,
    Action onChanged)
{
    public GlobalSystemMediaTransportControlsSession Session { get; } = session;

    public void Attach()
    {
        Session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        Session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        Session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
    }

    public void Detach()
    {
        try
        {
            Session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            Session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            Session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        catch (Exception ex) when (ex is COMException or ObjectDisposedException or InvalidOperationException)
        {
            // Ignore already-disconnected WinRT session.
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        onChanged();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        onChanged();

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        onChanged();
}
