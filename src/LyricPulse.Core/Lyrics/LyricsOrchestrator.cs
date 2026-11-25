using System.Collections.Concurrent;
using System.Collections.Immutable;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;
using Microsoft.Extensions.Logging;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Multi-provider lyrics orchestrator with in-flight request coalescing and deterministic waterfall:
/// Local Sidecar -> Sharded Disk Cache -> Ordered Online Providers (LRCLIB -> NetEase) -> Plain Fallback -> 24h Negative Cache.
/// </summary>
public sealed class LyricsOrchestrator : ILyricsOrchestrator
{
    private static readonly TimeSpan PipelineTimeout = TimeSpan.FromSeconds(10);

    private readonly LocalLrcSidecarProvider? _sidecarProvider;
    private readonly ILyricsCache _cache;
    private readonly ImmutableArray<ILyricsProvider> _providers;
    private readonly ILogger<LyricsOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, Task<LyricsResult>> _inFlight = new(StringComparer.Ordinal);

    public LyricsOrchestrator(
        LocalLrcSidecarProvider? sidecarProvider,
        ILyricsCache cache,
        IEnumerable<ILyricsProvider> providers,
        ILogger<LyricsOrchestrator> logger)
    {
        _sidecarProvider = sidecarProvider;
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers)))
            .OrderBy(p => p.PriorityOrder)
            .ToImmutableArray();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<LyricsResult> GetLyricsAsync(
        TrackIdentity track,
        CancellationToken cancellationToken = default) =>
        GetLyricsAsync(track, forceRefresh: false, cancellationToken);

    public Task<LyricsResult> GetLyricsAsync(
        TrackIdentity track,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!track.IsValid)
        {
            return Task.FromResult(new LyricsResult(LyricsStatus.NotFound, null));
        }

        var flightKey = forceRefresh ? $"{track.CacheKey}:refresh" : track.CacheKey;
        var sharedTask = _inFlight.GetOrAdd(flightKey, _ => ExecuteCoalescedFetchAsync(track, flightKey, forceRefresh));
        return cancellationToken.CanBeCanceled ? sharedTask.WaitAsync(cancellationToken) : sharedTask;
    }

    public Task InvalidateCacheAsync(TrackIdentity track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        return _cache.RemoveAsync(track, cancellationToken);
    }

    private async Task<LyricsResult> ExecuteCoalescedFetchAsync(TrackIdentity track, string flightKey, bool forceRefresh)
    {
        using var pipelineCts = new CancellationTokenSource(PipelineTimeout);
        var ct = pipelineCts.Token;

        try
        {
            // Priority 0: Local .lrc sidecar override (never written to cache so live file edits apply immediately).
            if (_sidecarProvider is not null)
            {
                var sidecarResult = await _sidecarProvider.FetchAsync(track, ct).ConfigureAwait(false);
                if (sidecarResult.Status is LyricsStatus.Synced or LyricsStatus.PlainOnly)
                {
                    return sidecarResult;
                }
            }

            // Priority 1: Sharded disk cache lookup (bypassed and purged when forceRefresh is requested).
            if (forceRefresh)
            {
                await _cache.RemoveAsync(track, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                var cached = await _cache.TryGetAsync(track, ct).ConfigureAwait(false);
                if (cached is not null)
                {
                    return cached;
                }
            }

            // Priority 2+: Ordered remote providers (LRCLIB -> NetEase).
            LyricsResult? bestPlainFallback = null;
            var hadProviderError = false;

            foreach (var provider in _providers)
            {
                LyricsResult candidate;
                try
                {
                    candidate = await provider.FetchAsync(track, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    hadProviderError = true;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogOrchestratorProviderException(ex, provider.Name);
                    hadProviderError = true;
                    continue;
                }

                if (candidate.Status is LyricsStatus.Synced or LyricsStatus.Instrumental)
                {
                    await _cache.SetAsync(track, candidate, CancellationToken.None).ConfigureAwait(false);
                    return candidate;
                }

                if (candidate.Status == LyricsStatus.PlainOnly && bestPlainFallback is null)
                {
                    bestPlainFallback = candidate;
                }
                else if (candidate.Status == LyricsStatus.Error)
                {
                    hadProviderError = true;
                }
            }

            if (bestPlainFallback is not null)
            {
                await _cache.SetAsync(track, bestPlainFallback, CancellationToken.None).ConfigureAwait(false);
                return bestPlainFallback;
            }

            if (hadProviderError)
            {
                return new LyricsResult(LyricsStatus.Error, null, "One or more lyrics providers failed transiently.");
            }

            var notFound = new LyricsResult(LyricsStatus.NotFound, null);
            await _cache.SetAsync(track, notFound, CancellationToken.None).ConfigureAwait(false);
            return notFound;
        }
        finally
        {
            _inFlight.TryRemove(flightKey, out _);
        }
    }
}
