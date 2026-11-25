using System.Globalization;
using System.Text;
using System.Text.Json;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Lyrics.Dtos;
using LyricPulse.Core.Models;
using LyricPulse.Core.Serialization;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Sharded, atomic local disk cache for fetched lyrics with tiered TTLs
/// (90d Synced, 30d Instrumental, 7d Plain, 24h NotFound) and 50 MB / 5,000-file LRU eviction.
/// </summary>
public sealed class DiskLyricsCache : ILyricsCache
{
    public const int CurrentSchemaVersion = 1;
    public const long MaxCacheBytes = 50L * 1024 * 1024;
    public const long TargetEvictBytes = 40L * 1024 * 1024;
    public const int MaxCacheFiles = 5000;

    public static readonly TimeSpan SyncedTtl = TimeSpan.FromDays(90);
    public static readonly TimeSpan InstrumentalTtl = TimeSpan.FromDays(30);
    public static readonly TimeSpan PlainTtl = TimeSpan.FromDays(7);
    public static readonly TimeSpan NotFoundTtl = TimeSpan.FromHours(24);

    private readonly string _rootFullPath;
    private readonly ILyricsParser _parser;
    private readonly Func<DateTimeOffset> _utcNowProvider;

    public DiskLyricsCache(
        string? cacheDirectory,
        ILyricsParser parser,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        var baseDir = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LyricPulse",
                "cache",
                "lyrics")
            : cacheDirectory;

        _rootFullPath = Path.GetFullPath(baseDir);
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _utcNowProvider = utcNowProvider ?? (static () => DateTimeOffset.UtcNow);
    }

    public async Task<LyricsResult?> TryGetAsync(TrackIdentity track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!TryResolveSafeCachePath(track.CacheKey, out var filePath) || !File.Exists(filePath))
        {
            return null;
        }

        CachedLyricsEnvelopeDto? envelope;
        try
        {
            await using var stream = File.OpenRead(filePath);
            envelope = await JsonSerializer.DeserializeAsync(
                stream,
                LyricPulseJsonContext.Default.CachedLyricsEnvelopeDto,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            TryDeleteQuietly(filePath);
            return null;
        }

        if (envelope is null || envelope.SchemaVersion != CurrentSchemaVersion || _utcNowProvider() >= envelope.ExpiresAtUtc)
        {
            TryDeleteQuietly(filePath);
            return null;
        }

        return RehydrateResult(track, envelope);
    }

    public async Task SetAsync(TrackIdentity track, LyricsResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status is LyricsStatus.Error or LyricsStatus.Loading ||
            !TryResolveSafeCachePath(track.CacheKey, out var filePath))
        {
            return;
        }

        var now = _utcNowProvider();
        var ttl = result.Status switch
        {
            LyricsStatus.Synced => SyncedTtl,
            LyricsStatus.Instrumental => InstrumentalTtl,
            LyricsStatus.PlainOnly => PlainTtl,
            _ => NotFoundTtl
        };

        var (format, rawPayload, translationPayload, romanizedPayload) = SerializeDocumentPayloads(result);
        var envelope = new CachedLyricsEnvelopeDto(
            SchemaVersion: CurrentSchemaVersion,
            CacheKey: track.CacheKey,
            CachedAtUtc: now,
            ExpiresAtUtc: now + ttl,
            Status: result.Status.ToString(),
            SourceKind: (result.Document?.SourceKind ?? LyricsSourceKind.DiskCache).ToString(),
            Provider: result.Document?.ProviderName ?? "Cache",
            Format: format,
            RawPayload: rawPayload,
            TranslationPayload: translationPayload,
            RomanizedPayload: romanizedPayload,
            MatchedTrack: track.NormalizedTitle,
            MatchedArtist: track.PrimaryArtist,
            MatchedDurationSec: track.Duration.TotalSeconds > 0 ? track.Duration.TotalSeconds : null);

        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    fs,
                    envelope,
                    LyricPulseJsonContext.Default.CachedLyricsEnvelopeDto,
                    cancellationToken).ConfigureAwait(false);
                await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            TryDeleteQuietly(tempPath);
        }
    }

    public Task RemoveAsync(TrackIdentity track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (TryResolveSafeCachePath(track.CacheKey, out var filePath))
        {
            TryDeleteQuietly(filePath);
        }

        return Task.CompletedTask;
    }

    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootFullPath))
        {
            return;
        }

        var now = _utcNowProvider();
        var surviving = new List<FileInfo>();
        long totalBytes = 0;

        foreach (var path in Directory.EnumerateFiles(_rootFullPath, "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                continue;
            }

            if (await IsExpiredAsync(path, now, cancellationToken).ConfigureAwait(false))
            {
                TryDeleteQuietly(path);
                continue;
            }

            surviving.Add(info);
            totalBytes += info.Length;
        }

        if (totalBytes <= MaxCacheBytes && surviving.Count <= MaxCacheFiles)
        {
            return;
        }

        surviving.Sort(static (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
        var remainingCount = surviving.Count;

        foreach (var file in surviving)
        {
            if (totalBytes <= TargetEvictBytes && remainingCount <= MaxCacheFiles)
            {
                break;
            }

            totalBytes -= file.Length;
            remainingCount--;
            TryDeleteQuietly(file.FullName);
        }
    }

    private static async Task<bool> IsExpiredAsync(string path, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var envelope = await JsonSerializer.DeserializeAsync(
                stream,
                LyricPulseJsonContext.Default.CachedLyricsEnvelopeDto,
                ct).ConfigureAwait(false);
            return envelope is null || now >= envelope.ExpiresAtUtc;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return true;
        }
    }

    private LyricsResult? RehydrateResult(TrackIdentity track, CachedLyricsEnvelopeDto envelope)
    {
        if (!Enum.TryParse<LyricsStatus>(envelope.Status, ignoreCase: true, out var status))
        {
            return null;
        }

        if (status is LyricsStatus.NotFound or LyricsStatus.Instrumental)
        {
            return new LyricsResult(status, null);
        }

        if (string.IsNullOrWhiteSpace(envelope.RawPayload))
        {
            return null;
        }

        var sourceKind = Enum.TryParse<LyricsSourceKind>(envelope.SourceKind, ignoreCase: true, out var parsedSource)
            ? parsedSource
            : LyricsSourceKind.DiskCache;

        LyricsDocument doc = envelope.Format switch
        {
            "Plain" => _parser.CreatePlain(track, envelope.RawPayload, sourceKind, envelope.Provider, isFromCache: true),
            _ => _parser.ParseLrc(
                track,
                envelope.RawPayload,
                sourceKind,
                envelope.Provider,
                isFromCache: true,
                envelope.TranslationPayload,
                envelope.RomanizedPayload)
        };

        return new LyricsResult(status, doc);
    }

    private static (string Format, string? Raw, string? Translation, string? Romanized) SerializeDocumentPayloads(LyricsResult result)
    {
        if (result.Document is null || result.Status is LyricsStatus.NotFound or LyricsStatus.Instrumental)
        {
            return ("None", null, null, null);
        }

        var doc = result.Document;
        if (result.Status == LyricsStatus.PlainOnly || doc.TimingKind == TimingKind.PlainUnsynced)
        {
            var plain = string.Join('\n', doc.Lines.Select(l => l.RawText));
            return ("Plain", plain, null, null);
        }

        var mainSb = new StringBuilder();
        var transSb = new StringBuilder();
        var romaSb = new StringBuilder();
        var isWordNative = doc.TimingKind == TimingKind.WordNative;

        foreach (var line in doc.Lines)
        {
            var lineTag = FormatBracketTimestamp(line.StartTime);
            mainSb.Append(lineTag);

            if (isWordNative && !line.Words.IsEmpty)
            {
                for (var i = 0; i < line.Words.Length; i++)
                {
                    var w = line.Words[i];
                    mainSb.Append(FormatAngleTimestamp(w.StartTime));
                    mainSb.Append(w.Text);
                    if (i + 1 < line.Words.Length && w.IsSpaceSeparated)
                    {
                        mainSb.Append(' ');
                    }
                }
            }
            else
            {
                mainSb.Append(line.RawText);
            }

            mainSb.Append('\n');

            if (!string.IsNullOrWhiteSpace(line.TranslationText))
            {
                transSb.Append(lineTag).Append(line.TranslationText).Append('\n');
            }

            if (!string.IsNullOrWhiteSpace(line.RomanizedText))
            {
                romaSb.Append(lineTag).Append(line.RomanizedText).Append('\n');
            }
        }

        return (
            isWordNative ? "EnhancedLrc" : "Lrc",
            mainSb.ToString(),
            transSb.Length > 0 ? transSb.ToString() : null,
            romaSb.Length > 0 ? romaSb.ToString() : null);
    }

    private static string FormatBracketTimestamp(TimeSpan ts)
    {
        var totalMinutes = (int)ts.TotalMinutes;
        return string.Create(CultureInfo.InvariantCulture, $"[{totalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}]");
    }

    private static string FormatAngleTimestamp(TimeSpan ts)
    {
        var totalMinutes = (int)ts.TotalMinutes;
        return string.Create(CultureInfo.InvariantCulture, $"<{totalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}>");
    }

    private bool TryResolveSafeCachePath(string cacheKey, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(cacheKey) || cacheKey.Length < 4)
        {
            return false;
        }

        foreach (var ch in cacheKey)
        {
            var isHex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        var lower = cacheKey.ToLowerInvariant();
        var shard = lower[..2];
        var candidate = Path.GetFullPath(Path.Combine(_rootFullPath, shard, $"{lower}.json"));
        var rootWithSep = _rootFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootFullPath
            : _rootFullPath + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Ignore transient file locks during cache cleanup.
        }
    }
}
