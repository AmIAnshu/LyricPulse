using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using LyricPulse.Core.Lyrics.Dtos;
using LyricPulse.Core.Models;

namespace LyricPulse.Core.Serialization;

/// <summary>
/// AOT-safe System.Text.Json source-generated serialization context covering
/// settings, track offsets, lyrics cache envelopes, and provider HTTP DTOs.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(StageBackdropMode))]
[JsonSerializable(typeof(EdgeGlowMode))]
[JsonSerializable(typeof(AudioVisualizerMode))]
[JsonSerializable(typeof(OverlayBoundsConfig))]
[JsonSerializable(typeof(RgbColor))]
[JsonSerializable(typeof(TrackOffsetsEnvelopeDto))]
[JsonSerializable(typeof(TrackOffsetEntryDto))]
[JsonSerializable(typeof(CachedLyricsEnvelopeDto))]
[JsonSerializable(typeof(LrclibTrackDto))]
[JsonSerializable(typeof(ImmutableArray<LrclibTrackDto>))]
[JsonSerializable(typeof(LrclibErrorDto))]
[JsonSerializable(typeof(NetEaseSearchResponseDto))]
[JsonSerializable(typeof(NetEaseLyricResponseDto))]
public sealed partial class LyricPulseJsonContext : JsonSerializerContext
{
    public const int MaxSecureJsonDepth = 16;

    /// <summary>
    /// Depth-capped (MaxDepth = 16) AOT-safe context for untrusted HTTP payloads and local disk JSON files.
    /// </summary>
    public static LyricPulseJsonContext Secure { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        MaxDepth = MaxSecureJsonDepth
    });
}

/// <summary>
/// Pretty-printed AOT-safe JSON context for human-readable settings and offset files.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(TrackOffsetsEnvelopeDto))]
public sealed partial class LyricPulseIndentedJsonContext : JsonSerializerContext;
