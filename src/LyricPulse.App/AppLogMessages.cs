using Microsoft.Extensions.Logging;

namespace LyricPulse.App;

/// <summary>
/// Source-generated [LoggerMessage] delegates for desktop host, hotkey, and tray diagnostics.
/// </summary>
internal static partial class AppLogMessages
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Global hotkey registration conflict for {Action}: {Reason}")]
    public static partial void LogHotkeyConflict(this ILogger logger, string action, string reason);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Error, Message = "Desktop host startup encountered an unexpected error")]
    public static partial void LogHostStartupError(this ILogger logger, Exception ex);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "Async desktop host action '{ActionName}' failed")]
    public static partial void LogHostActionError(this ILogger logger, Exception ex, string actionName);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Warning, Message = "Runtime shutdown timed out after {TimeoutMs}ms; continuing exit cleanup")]
    public static partial void LogShutdownTimeout(this ILogger logger, int timeoutMs);
}
