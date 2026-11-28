using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Reads and deserializes HTTP JSON responses with a strict hard byte limit so a malicious
/// or compromised upstream cannot trigger unbounded memory growth (remote OOM DoS).
/// </summary>
internal static class BoundedHttpResponseReader
{
    public const int DefaultMaxJsonBytes = 2 * 1024 * 1024; // 2 MB

    public static async Task<(bool ExceededLimit, T? Value)> ReadJsonAsync<T>(
        HttpResponseMessage response,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken,
        int maxBytes = DefaultMaxJsonBytes)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength.HasValue && declaredLength.Value > maxBytes)
        {
            return (true, default);
        }

        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var initialCapacity = declaredLength is > 0 and <= 65_536 ? (int)declaredLength.Value : 8_192;
        using var bufferStream = new MemoryStream(initialCapacity);
        var chunk = new byte[16_384];
        var totalRead = 0;

        while (true)
        {
            var read = await sourceStream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > maxBytes)
            {
                return (true, default);
            }

            bufferStream.Write(chunk, 0, read);
        }

        if (totalRead == 0)
        {
            return (false, default);
        }

        bufferStream.Position = 0;
        var parsed = await JsonSerializer.DeserializeAsync(bufferStream, jsonTypeInfo, cancellationToken)
            .ConfigureAwait(false);
        return (false, parsed);
    }
}
