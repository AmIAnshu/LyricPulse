using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;
using LyricPulse.Core.Security;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Priority-0 local .lrc sidecar override provider scanning %LOCALAPPDATA%\LyricPulse\custom-lyrics\.
/// </summary>
public sealed class LocalLrcSidecarProvider : ILyricsProvider
{
    public const int MaxSanitizedStemChars = 160;

    private readonly string _rootFullPath;
    private readonly ILyricsParser _parser;

    public LocalLrcSidecarProvider(string? sidecarDirectory, ILyricsParser parser)
    {
        var dir = string.IsNullOrWhiteSpace(sidecarDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LyricPulse",
                "custom-lyrics")
            : sidecarDirectory;

        _rootFullPath = Path.GetFullPath(dir);
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public string Name => "LocalSidecar";
    public LyricsSourceKind SourceKind => LyricsSourceKind.LocalSidecar;
    public int PriorityOrder => 0;

    public async Task<LyricsResult> FetchAsync(TrackIdentity track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!track.IsValid || !Directory.Exists(_rootFullPath))
        {
            return new LyricsResult(LyricsStatus.NotFound, null);
        }

        foreach (var fileName in BuildCandidateFileNames(track))
        {
            if (!TryResolveSafePath(fileName, out var fullPath) ||
                !SafeFileGuard.TryOpenSafeReadStream(fullPath, SafeFileGuard.MaxSidecarLrcFileBytes, out var stream))
            {
                continue;
            }

            string content;
            await using (stream!)
            using (var reader = new StreamReader(stream))
            {
                content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var doc = _parser.ParseLrc(track, content, SourceKind, Name, isFromCache: false);
            if (!doc.Lines.IsEmpty)
            {
                var status = doc.TimingKind == TimingKind.PlainUnsynced
                    ? LyricsStatus.PlainOnly
                    : LyricsStatus.Synced;
                return new LyricsResult(status, doc);
            }
        }

        return new LyricsResult(LyricsStatus.NotFound, null);
    }

    private static IEnumerable<string> BuildCandidateFileNames(TrackIdentity track)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(track.CacheKey))
        {
            var stem = SanitizeFileName(track.CacheKey);
            if (stem.Length > 0 && seen.Add($"{stem}.lrc"))
            {
                yield return $"{stem}.lrc";
            }
        }

        if (!string.IsNullOrWhiteSpace(track.PrimaryArtist) && !string.IsNullOrWhiteSpace(track.NormalizedTitle))
        {
            var stem = SanitizeFileName($"{track.PrimaryArtist} - {track.NormalizedTitle}");
            if (stem.Length > 0 && seen.Add($"{stem}.lrc"))
            {
                yield return $"{stem}.lrc";
            }
        }

        if (!string.IsNullOrWhiteSpace(track.RawArtist) && !string.IsNullOrWhiteSpace(track.RawTitle))
        {
            var stem = SanitizeFileName($"{track.RawArtist} - {track.RawTitle}");
            if (stem.Length > 0 && seen.Add($"{stem}.lrc"))
            {
                yield return $"{stem}.lrc";
            }
        }

        if (!string.IsNullOrWhiteSpace(track.NormalizedTitle))
        {
            var stem = SanitizeFileName(track.NormalizedTitle);
            if (stem.Length > 0 && seen.Add($"{stem}.lrc"))
            {
                yield return $"{stem}.lrc";
            }
        }
    }

    internal static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            if (Array.IndexOf(invalid, ch) >= 0 || char.IsControl(ch) || ch is '/' or '\\')
            {
                chars[i] = '_';
            }
        }

        var sanitized = new string(chars);
        while (sanitized.Contains("..", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("..", "_", StringComparison.Ordinal);
        }

        sanitized = sanitized.Trim().TrimEnd('.', ' ');
        if (sanitized.Length > MaxSanitizedStemChars)
        {
            sanitized = sanitized[..MaxSanitizedStemChars].TrimEnd('.', ' ');
        }

        if (sanitized.Length == 0)
        {
            return string.Empty;
        }

        if (SafeFileGuard.IsReservedWindowsDeviceName(sanitized))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    private bool TryResolveSafePath(string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..", StringComparison.Ordinal) ||
            fileName.IndexOfAny(['/', '\\']) >= 0 ||
            SafeFileGuard.IsReservedWindowsDeviceName(fileName))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_rootFullPath, fileName));
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
}
