using System.Collections.Immutable;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Media;

/// <summary>
/// Deterministic media session selector implementing player tier priority,
/// browser video filtering, and 1,500ms track-transition hysteresis.
/// </summary>
public sealed class MediaSessionSelector
{
    public static readonly TimeSpan TransitionHysteresisWindow = TimeSpan.FromMilliseconds(1500);

    private string? _activeAumid;
    private MediaPlaybackState _activeLastState = MediaPlaybackState.Closed;
    private DateTimeOffset? _transitionGraceStartedUtc;

    public string? ActiveAumid => _activeAumid;

    public SessionCandidate? Select(
        ImmutableArray<SessionCandidate> candidates,
        MediaSourcePolicy policy,
        bool ignoreBrowserVideoSessions,
        DateTimeOffset nowUtc)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            ResetActive();
            return null;
        }

        var stickyCandidate = FindStickyCandidate(candidates, policy, ignoreBrowserVideoSessions);
        if (stickyCandidate is not null && ShouldHoldDuringTransition(stickyCandidate, nowUtc))
        {
            return stickyCandidate;
        }

        var eligible = FilterEligible(candidates, policy, ignoreBrowserVideoSessions, nowUtc);
        if (eligible.Count == 0)
        {
            ResetActive();
            return null;
        }

        var playingWinner = SelectBestPlaying(eligible);
        if (playingWinner is not null)
        {
            UpdateActiveState(playingWinner, nowUtc);
            return playingWinner;
        }

        // Zero sessions playing: retain active paused/changing session if still present.
        var retained = eligible.FirstOrDefault(c =>
            _activeAumid is not null &&
            c.SourceAppUserModelId.Equals(_activeAumid, StringComparison.OrdinalIgnoreCase) &&
            c.PlaybackState is MediaPlaybackState.Paused or MediaPlaybackState.Changing);

        if (retained is not null)
        {
            UpdateActiveState(retained, nowUtc);
            return retained;
        }

        var bestPaused = SelectBestPaused(eligible);
        if (bestPaused is not null)
        {
            UpdateActiveState(bestPaused, nowUtc);
            return bestPaused;
        }

        ResetActive();
        return null;
    }

    private SessionCandidate? FindStickyCandidate(
        ImmutableArray<SessionCandidate> candidates,
        MediaSourcePolicy policy,
        bool ignoreBrowserVideoSessions)
    {
        if (_activeAumid is null)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            if (!candidate.SourceAppUserModelId.Equals(_activeAumid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsPolicyEligiblePlayer(candidate, policy, ignoreBrowserVideoSessions) &&
                candidate.PlaybackState is not (MediaPlaybackState.Closed or MediaPlaybackState.Opened))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool ShouldHoldDuringTransition(SessionCandidate sticky, DateTimeOffset nowUtc)
    {
        var isTransitioning = sticky.PlaybackState == MediaPlaybackState.Changing ||
                              (string.IsNullOrWhiteSpace(sticky.Title) &&
                               _activeLastState is MediaPlaybackState.Playing or MediaPlaybackState.Changing);

        if (!isTransitioning)
        {
            _transitionGraceStartedUtc = null;
            return false;
        }

        _transitionGraceStartedUtc ??= nowUtc;
        var elapsed = nowUtc - _transitionGraceStartedUtc.Value;
        return elapsed >= TimeSpan.Zero && elapsed <= TransitionHysteresisWindow;
    }

    private List<SessionCandidate> FilterEligible(
        ImmutableArray<SessionCandidate> candidates,
        MediaSourcePolicy policy,
        bool ignoreBrowserVideoSessions,
        DateTimeOffset nowUtc)
    {
        var eligible = new List<SessionCandidate>(candidates.Length);

        foreach (var candidate in candidates)
        {
            if (candidate.PlaybackState is MediaPlaybackState.Closed or MediaPlaybackState.Opened)
            {
                continue;
            }

            if (!IsPolicyEligiblePlayer(candidate, policy, ignoreBrowserVideoSessions))
            {
                continue;
            }

            var isStickyInGrace = _activeAumid is not null &&
                                  candidate.SourceAppUserModelId.Equals(_activeAumid, StringComparison.OrdinalIgnoreCase) &&
                                  _transitionGraceStartedUtc.HasValue &&
                                  (nowUtc - _transitionGraceStartedUtc.Value) <= TransitionHysteresisWindow;

            if (string.IsNullOrWhiteSpace(candidate.Title) && !isStickyInGrace)
            {
                continue;
            }

            eligible.Add(candidate);
        }

        return eligible;
    }

    private static bool IsPolicyEligiblePlayer(
        SessionCandidate candidate,
        MediaSourcePolicy policy,
        bool ignoreBrowserVideoSessions)
    {
        if (candidate.PlayerKind is MediaPlayerKind.Ignored or MediaPlayerKind.Unknown)
        {
            return false;
        }

        if (policy == MediaSourcePolicy.SupportedPlayersOnly &&
            candidate.PlayerKind is not (MediaPlayerKind.Spotify or MediaPlayerKind.AppleMusic))
        {
            return false;
        }

        if (policy == MediaSourcePolicy.UniversalFallback &&
            ignoreBrowserVideoSessions &&
            candidate.PlayerKind == MediaPlayerKind.BrowserOrGeneric &&
            candidate.ItemKind == MediaItemKind.Video)
        {
            return false;
        }

        return true;
    }

    private SessionCandidate? SelectBestPlaying(List<SessionCandidate> eligible) =>
        eligible
            .Where(c => c.PlaybackState == MediaPlaybackState.Playing)
            .OrderByDescending(c => PlayerClassifier.GetPriorityWeight(c.PlayerKind))
            .ThenByDescending(c => c.IsSystemCurrentSession ? 1 : 0)
            .ThenByDescending(c => _activeAumid is not null &&
                                   c.SourceAppUserModelId.Equals(_activeAumid, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(c => !string.IsNullOrWhiteSpace(c.Artist) ? 1 : 0)
            .ThenByDescending(c => c.LastUpdatedUtc)
            .FirstOrDefault();

    private static SessionCandidate? SelectBestPaused(List<SessionCandidate> eligible) =>
        eligible
            .Where(c => c.PlaybackState == MediaPlaybackState.Paused &&
                        !string.IsNullOrWhiteSpace(c.Title))
            .OrderByDescending(c => PlayerClassifier.GetPriorityWeight(c.PlayerKind))
            .ThenByDescending(c => c.IsSystemCurrentSession ? 1 : 0)
            .ThenByDescending(c => !string.IsNullOrWhiteSpace(c.Artist) ? 1 : 0)
            .ThenByDescending(c => c.LastUpdatedUtc)
            .FirstOrDefault();

    private void UpdateActiveState(SessionCandidate selected, DateTimeOffset nowUtc)
    {
        if (!string.Equals(_activeAumid, selected.SourceAppUserModelId, StringComparison.OrdinalIgnoreCase))
        {
            _transitionGraceStartedUtc = null;
        }
        else if (selected.PlaybackState == MediaPlaybackState.Playing && !string.IsNullOrWhiteSpace(selected.Title))
        {
            _transitionGraceStartedUtc = null;
        }
        else if (selected.PlaybackState == MediaPlaybackState.Changing)
        {
            _transitionGraceStartedUtc ??= nowUtc;
        }

        _activeAumid = selected.SourceAppUserModelId;
        _activeLastState = selected.PlaybackState;
    }

    private void ResetActive()
    {
        _activeAumid = null;
        _activeLastState = MediaPlaybackState.Closed;
        _transitionGraceStartedUtc = null;
    }
}
