using System.Diagnostics.CodeAnalysis;

namespace LyricPulse.Core.Security;

/// <summary>
/// Centralized local-file security guard enforcing hard file-size ceilings,
/// reparse-point / symlink rejection, and Windows reserved device-name checks.
/// </summary>
public static class SafeFileGuard
{
    public const long MaxSettingsFileBytes = 512L * 1024;
    public const long MaxTrackOffsetsFileBytes = 1024L * 1024;
    public const long MaxSidecarLrcFileBytes = 1024L * 1024;
    public const long MaxLyricsCacheFileBytes = 2L * 1024 * 1024;
    public const long MaxGloveVocabFileBytes = 32L * 1024 * 1024;

    private static readonly HashSet<string> ReservedWindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool IsReservedWindowsDeviceName(string? fileNameOrBaseName)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrBaseName))
        {
            return false;
        }

        var trimmed = fileNameOrBaseName.Trim().TrimEnd('.');
        var dotIdx = trimmed.IndexOf('.');
        var stem = dotIdx >= 0 ? trimmed[..dotIdx] : trimmed;
        return ReservedWindowsDeviceNames.Contains(stem);
    }

    public static bool IsReparsePointOrSymlink(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }

            return (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                   !string.IsNullOrEmpty(info.LinkTarget);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return true;
        }
    }

    public static bool TryOpenSafeReadStream(string path, long maxBytes, [NotNullWhen(true)] out FileStream? stream)
    {
        stream = null;
        if (string.IsNullOrWhiteSpace(path) || maxBytes <= 0)
        {
            return false;
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }

            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || !string.IsNullOrEmpty(info.LinkTarget))
            {
                return false;
            }

            if (info.Length > maxBytes)
            {
                return false;
            }

            var opened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            if (opened.Length > maxBytes)
            {
                opened.Dispose();
                return false;
            }

            stream = opened;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
