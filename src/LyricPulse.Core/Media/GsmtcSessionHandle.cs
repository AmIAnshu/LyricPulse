using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Windows.Media.Control;

namespace LyricPulse.Core.Media;

/// <summary>
/// Manages WinRT event handler attachment, cached track metadata fallback across transient COM glitches,
/// and explicit COM-safe detachment for a single tracked GSMTC session.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin WinRT COM event wrapper.")]
internal sealed class GsmtcSessionHandle(
    GlobalSystemMediaTransportControlsSession session,
    Action onChanged)
{
    private readonly Lock _cacheLock = new();
    private bool _mediaPropertiesDirty = true;
    private DateTimeOffset _lastMetadataFetchUtc = DateTimeOffset.MinValue;
    private string _cachedTitle = string.Empty;
    private string _cachedArtist = string.Empty;
    private string _cachedAlbum = string.Empty;

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

    public bool ShouldFetchMediaProperties(DateTimeOffset nowUtc, bool isChangingState)
    {
        lock (_cacheLock)
        {
            if (_mediaPropertiesDirty || isChangingState || string.IsNullOrWhiteSpace(_cachedTitle))
            {
                return true;
            }

            return (nowUtc - _lastMetadataFetchUtc) >= TimeSpan.FromSeconds(3.0);
        }
    }

    public void UpdateCachedMediaProperties(string? title, string? artist, string? album, DateTimeOffset nowUtc)
    {
        lock (_cacheLock)
        {
            _lastMetadataFetchUtc = nowUtc;
            if (!string.IsNullOrWhiteSpace(title))
            {
                _cachedTitle = title;
                _cachedArtist = artist ?? string.Empty;
                _cachedAlbum = album ?? string.Empty;
                _mediaPropertiesDirty = false;
            }
        }
    }

    public (string Title, string Artist, string Album) GetCachedMediaProperties()
    {
        lock (_cacheLock)
        {
            return (_cachedTitle, _cachedArtist, _cachedAlbum);
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        lock (_cacheLock)
        {
            _mediaPropertiesDirty = true;
        }

        onChanged();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        onChanged();

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        onChanged();
}
