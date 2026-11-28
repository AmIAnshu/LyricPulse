using System.Text.Json;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;
using LyricPulse.Core.Security;
using LyricPulse.Core.Serialization;

namespace LyricPulse.Core.Settings;

/// <summary>
/// Atomic, AOT-safe JSON settings store backed by %LOCALAPPDATA%\LyricPulse\settings.json.
/// Uses temp-file flush + File.Move replacement and recovers cleanly from corrupt files.
/// </summary>
public sealed partial class JsonSettingsStore : ISettingsStore, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private volatile AppSettings _current = AppSettings.Default;

    public void Dispose() => _ioLock.Dispose();

    public JsonSettingsStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LyricPulse",
                "settings.json")
            : filePath;
    }

    public AppSettings Current => _current;

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!SafeFileGuard.TryOpenSafeReadStream(_filePath, SafeFileGuard.MaxSettingsFileBytes, out var stream))
            {
                _current = AppSettings.Default;
                return _current;
            }

            try
            {
                await using (stream!)
                {
                    var loaded = await JsonSerializer.DeserializeAsync(
                        stream,
                        LyricPulseJsonContext.Secure.AppSettings,
                        cancellationToken).ConfigureAwait(false);

                    _current = (loaded ?? AppSettings.Default).ValidateAndNormalize();
                    return _current;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _current = AppSettings.Default;
                return _current;
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SaveAsync(AppSettings updatedSettings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updatedSettings);
        var normalized = updatedSettings.ValidateAndNormalize();

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PersistAtomicAsync(normalized, cancellationToken).ConfigureAwait(false);
            _current = normalized;
        }
        finally
        {
            _ioLock.Release();
        }

        SettingsChanged?.Invoke(this, normalized);
    }

    public async Task<AppSettings> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var defaults = AppSettings.Default;
        await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
        return defaults;
    }

    private async Task PersistAtomicAsync(AppSettings settings, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    fs,
                    settings,
                    LyricPulseIndentedJsonContext.Default.AppSettings,
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
}
