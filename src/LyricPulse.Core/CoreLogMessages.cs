using Microsoft.Extensions.Logging;

namespace LyricPulse.Core;

/// <summary>
/// Compile-time source-generated [LoggerMessage] delegates for zero-allocation structured logging.
/// </summary>
internal static partial class CoreLogMessages
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "GSMTC SessionManager initialization failed on attempt {Attempt}")]
    public static partial void LogGsmtcInitFailed(this ILogger logger, Exception ex, int attempt);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Transient GSMTC error while enumerating sessions")]
    public static partial void LogGsmtcEnumerateError(this ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Rejected oversized GSMTC thumbnail ({Bytes} bytes > 10 MB cap)")]
    public static partial void LogGsmtcOversizedThumbnail(this ILogger logger, ulong bytes);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Aborted GSMTC thumbnail read exceeding 10 MB hard cap")]
    public static partial void LogGsmtcAbortedThumbnail(this ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Debug, Message = "Transient GSMTC COM exception during {Operation}")]
    public static partial void LogGsmtcComException(this ILogger logger, Exception ex, string operation);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "{Provider} skipped because cooldown circuit breaker is active")]
    public static partial void LogProviderInCooldown(this ILogger logger, string provider);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "{Provider} rate limiter rejected request")]
    public static partial void LogProviderRateLimited(this ILogger logger, string provider);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "{Provider} request timed out after retries")]
    public static partial void LogProviderTimedOut(this ILogger logger, string provider);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning, Message = "{Provider} HTTP request failed after retries")]
    public static partial void LogProviderHttpFailed(this ILogger logger, Exception ex, string provider);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning, Message = "{Provider} tripped cooldown for {Seconds:F1}s")]
    public static partial void LogProviderCooldownTripped(this ILogger logger, string provider, double seconds);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Warning, Message = "{Provider} returned malformed JSON for {Endpoint}")]
    public static partial void LogProviderMalformedJson(this ILogger logger, Exception ex, string provider, string endpoint);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Warning, Message = "Lyrics provider {Provider} threw an unexpected exception")]
    public static partial void LogOrchestratorProviderException(this ILogger logger, Exception ex, string provider);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Runtime track load failed unexpectedly for '{TrackTitle}'")]
    public static partial void LogRuntimeTrackLoadFailed(this ILogger logger, Exception ex, string trackTitle);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "Runtime album art palette extraction failed unexpectedly")]
    public static partial void LogRuntimeAlbumArtFailed(this ILogger logger, Exception ex);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Failed to load quantized GloVe visual embeddings from '{BinaryPath}'; falling back to StarterLexicon mode")]
    public static partial void LogGloveEmbeddingLoadFailed(this ILogger logger, Exception ex, string binaryPath);
}
