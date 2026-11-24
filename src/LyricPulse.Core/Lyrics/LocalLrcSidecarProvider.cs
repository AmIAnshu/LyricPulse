using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Priority-0 local .lrc sidecar override provider scanning %LOCALAPPDATA%\LyricPulse\custom-lyrics\.
/// </summary>
public sealed class LocalLrcSidecarProvider : ILyricsProvider
{
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
            if (!TryResolveSafePath(fileName, out var fullPath) || !File.Exists(fullPath))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
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
            var byHash = $"{SanitizeFileName(track.CacheKey)}.lrc";
            if (seen.Add(byHash))
            {
                yield return byHash;
            }
        }

        if (!string.IsNullOrWhiteSpace(track.PrimaryArtist) && !string.IsNullOrWhiteSpace(track.NormalizedTitle))
        {
            var byNorm = $"{SanitizeFileName($"{track.PrimaryArtist} - {track.NormalizedTitle}")}.lrc";
            if (seen.Add(byNorm))
            {
                yield return byNorm;
            }
        }

        if (!string.IsNullOrWhiteSpace(track.RawArtist) && !string.IsNullOrWhiteSpace(track.RawTitle))
        {
            var byRaw = $"{SanitizeFileName($"{track.RawArtist} - {track.RawTitle}")}.lrc";
            if (seen.Add(byRaw))
            {
                yield return byRaw;
            }
        }

        if (!string.IsNullOrWhiteSpace(track.NormalizedTitle))
        {
            var byTitle = $"{SanitizeFileName(track.NormalizedTitle)}.lrc";
            if (seen.Add(byTitle))
            {
                yield return byTitle;
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private bool TryResolveSafePath(string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
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
