using System.Collections.Immutable;
using System.Text.Json;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics.Dtos;
using LyricPulse.Core.Models;
using LyricPulse.Core.Security;
using LyricPulse.Core.Serialization;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Atomic JSON file store for per-track timing offsets with LRU eviction capped at 1,000 tracks.
/// </summary>
public sealed partial class TrackOffsetStore : ITrackOffsetStore, IDisposable
{
    public const int MaxEntries = 1000;
    private const int CurrentSchemaVersion = 1;

    private readonly string _filePath;
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private ImmutableDictionary<string, TrackOffsetEntryDto>? _cachedEntries;

    public void Dispose() => _ioLock.Dispose();

    public TrackOffsetStore(string? filePath = null, Func<DateTimeOffset>? utcNowProvider = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LyricPulse",
                "track-offsets.json")
            : filePath;
        _utcNowProvider = utcNowProvider ?? (static () => DateTimeOffset.UtcNow);
    }

    public async Task<TimeSpan?> TryGetOffsetAsync(string trackCacheKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackCacheKey))
        {
            return null;
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (!entries.TryGetValue(trackCacheKey, out var entry))
            {
                return null;
            }

            // Refresh in-memory access timestamp so LRU eviction reflects recency of reads as well as writes.
            _cachedEntries = entries.SetItem(trackCacheKey, entry with { LastAccessedUtc = _utcNowProvider() });
            return ClampOffset(TimeSpan.FromMilliseconds(entry.OffsetMs));
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SetOffsetAsync(string trackCacheKey, TimeSpan offset, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackCacheKey))
        {
            return;
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var clamped = ClampOffset(offset);
            var updated = entries.SetItem(
                trackCacheKey,
                new TrackOffsetEntryDto((int)Math.Round(clamped.TotalMilliseconds), _utcNowProvider()));

            if (updated.Count > MaxEntries)
            {
                updated = EvictLeastRecentlyUsed(updated);
            }

            _cachedEntries = updated;
            await PersistAtomicAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task RemoveOffsetAsync(string trackCacheKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackCacheKey))
        {
            return;
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (!entries.ContainsKey(trackCacheKey))
            {
                return;
            }

            var updated = entries.Remove(trackCacheKey);
            _cachedEntries = updated;
            await PersistAtomicAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task<ImmutableDictionary<string, TrackOffsetEntryDto>> EnsureLoadedAsync(CancellationToken ct)
    {
        if (_cachedEntries is not null)
        {
            return _cachedEntries;
        }

        if (!SafeFileGuard.TryOpenSafeReadStream(_filePath, SafeFileGuard.MaxTrackOffsetsFileBytes, out var stream))
        {
            _cachedEntries = ImmutableDictionary<string, TrackOffsetEntryDto>.Empty;
            return _cachedEntries;
        }

        try
        {
            await using (stream!)
            {
                var envelope = await JsonSerializer.DeserializeAsync(
                    stream,
                    LyricPulseJsonContext.Secure.TrackOffsetsEnvelopeDto,
                    ct).ConfigureAwait(false);

                var loaded = envelope?.Entries ?? ImmutableDictionary<string, TrackOffsetEntryDto>.Empty;
                _cachedEntries = loaded.Count > MaxEntries
                    ? EvictLeastRecentlyUsed(loaded)
                    : loaded;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _cachedEntries = ImmutableDictionary<string, TrackOffsetEntryDto>.Empty;
        }

        return _cachedEntries;
    }

    private async Task PersistAtomicAsync(
        ImmutableDictionary<string, TrackOffsetEntryDto> entries,
        CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        var envelope = new TrackOffsetsEnvelopeDto(CurrentSchemaVersion, entries);

        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    fs,
                    envelope,
                    LyricPulseIndentedJsonContext.Default.TrackOffsetsEnvelopeDto,
                    ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static ImmutableDictionary<string, TrackOffsetEntryDto> EvictLeastRecentlyUsed(
        ImmutableDictionary<string, TrackOffsetEntryDto> entries) =>
        entries
            .OrderByDescending(kv => kv.Value.LastAccessedUtc)
            .Take(MaxEntries)
            .ToImmutableDictionary(kv => kv.Key, kv => kv.Value);

    private static TimeSpan ClampOffset(TimeSpan offset) =>
        TimeSpan.FromTicks(Math.Clamp(offset.Ticks, TimingOffsetState.MinOffset.Ticks, TimingOffsetState.MaxOffset.Ticks));
}

/// <summary>
/// Manages per-song timing offset state with the locked canonical sign convention:
/// Negative offset (-500ms / -1000ms) = Lyrics appear EARLIER;
/// Positive offset (+500ms / +1000ms) = Lyrics appear LATER;
/// EffectiveLookupTime = Clamp(InterpolatedPosition - TimingOffset, 0, Duration).
/// </summary>
public sealed partial class TimingOffsetService : IDisposable
{
    private readonly ITrackOffsetStore _store;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private volatile TimingOffsetState _current = new(null, TimeSpan.Zero, false);

    public TimingOffsetService(ITrackOffsetStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public TimingOffsetState Current => _current;

    public event EventHandler<TimingOffsetState>? OffsetChanged;

    public void Dispose() => _stateLock.Dispose();

    /// <summary>
    /// Computes lookup time in lyric coordinates using the locked sign contract:
    /// EffectiveLookupTime = Clamp(Position - Offset, 0, Duration).
    /// </summary>
    public static TimeSpan ComputeEffectiveLookupTime(
        TimeSpan interpolatedPosition,
        TimeSpan timingOffset,
        TimeSpan trackDuration)
    {
        var effective = interpolatedPosition - timingOffset;
        if (effective < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (trackDuration > TimeSpan.Zero && effective > trackDuration)
        {
            return trackDuration;
        }

        return effective;
    }

    public TimeSpan GetEffectiveLookupTime(TimeSpan interpolatedPosition, TimeSpan trackDuration) =>
        ComputeEffectiveLookupTime(interpolatedPosition, _current.Offset, trackDuration);

    public async Task<TimingOffsetState> OnTrackChangedAsync(
        string? trackCacheKey,
        bool enablePerTrackSavedOffsets,
        CancellationToken cancellationToken = default)
    {
        TimingOffsetState published;
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(trackCacheKey) || !enablePerTrackSavedOffsets)
            {
                published = new TimingOffsetState(trackCacheKey, TimeSpan.Zero, IsPersistedOverride: false);
            }
            else
            {
                var saved = await _store.TryGetOffsetAsync(trackCacheKey, cancellationToken).ConfigureAwait(false);
                published = saved.HasValue && saved.Value != TimeSpan.Zero
                    ? new TimingOffsetState(trackCacheKey, saved.Value, IsPersistedOverride: true)
                    : new TimingOffsetState(trackCacheKey, TimeSpan.Zero, IsPersistedOverride: false);
            }

            _current = published;
        }
        finally
        {
            _stateLock.Release();
        }

        OffsetChanged?.Invoke(this, published);
        return published;
    }

    public async Task<TimingOffsetState> NudgeAsync(
        TimeSpan delta,
        bool enablePerTrackSavedOffsets,
        CancellationToken cancellationToken = default)
    {
        TimingOffsetState published;
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var nudged = _current.WithNudge(delta);
            var shouldPersist = enablePerTrackSavedOffsets && !string.IsNullOrWhiteSpace(nudged.TrackCacheKey);

            if (shouldPersist)
            {
                if (nudged.Offset == TimeSpan.Zero)
                {
                    await _store.RemoveOffsetAsync(nudged.TrackCacheKey!, cancellationToken).ConfigureAwait(false);
                    nudged = nudged with { IsPersistedOverride = false };
                }
                else
                {
                    await _store.SetOffsetAsync(nudged.TrackCacheKey!, nudged.Offset, cancellationToken).ConfigureAwait(false);
                    nudged = nudged with { IsPersistedOverride = true };
                }
            }
            else
            {
                nudged = nudged with { IsPersistedOverride = false };
            }

            _current = nudged;
            published = nudged;
        }
        finally
        {
            _stateLock.Release();
        }

        OffsetChanged?.Invoke(this, published);
        return published;
    }

    public async Task<TimingOffsetState> ResetAsync(
        bool enablePerTrackSavedOffsets,
        CancellationToken cancellationToken = default)
    {
        TimingOffsetState published;
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var reset = _current.WithReset() with { IsPersistedOverride = false };

            if (enablePerTrackSavedOffsets && !string.IsNullOrWhiteSpace(reset.TrackCacheKey))
            {
                await _store.RemoveOffsetAsync(reset.TrackCacheKey, cancellationToken).ConfigureAwait(false);
            }

            _current = reset;
            published = reset;
        }
        finally
        {
            _stateLock.Release();
        }

        OffsetChanged?.Invoke(this, published);
        return published;
    }
}
