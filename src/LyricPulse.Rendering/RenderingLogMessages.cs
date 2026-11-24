using Microsoft.Extensions.Logging;

namespace LyricPulse.Rendering;

/// <summary>
/// Source-generated [LoggerMessage] delegates for zero-allocation rendering diagnostics.
/// </summary>
internal static partial class RenderingLogMessages
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "SkSL shader '{ShaderName}' failed to compile; graceful unshaded fallback active. Error: {Error}")]
    public static partial void LogShaderCompileFailed(this ILogger logger, string shaderName, string error);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "Album art payload rejected ({Bytes} bytes > {MaxBytes} bytes cap)")]
    public static partial void LogAlbumArtOversizedBytes(this ILogger logger, int bytes, int maxBytes);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Album art rejected by decompression-bomb guard ({Width}x{Height} > {MaxPixels} pixels)")]
    public static partial void LogAlbumArtOversizedPixels(this ILogger logger, int width, int height, long maxPixels);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Debug, Message = "Album art failed to decode cleanly; using Studio default palette")]
    public static partial void LogAlbumArtDecodeFailed(this ILogger logger);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Warning, Message = "Offscreen shader GPU P95 exceeded {P95Ms:F1}ms; adaptive resolution scale stepping down")]
    public static partial void LogAdaptiveScaleStepDown(this ILogger logger, double p95Ms);
}
