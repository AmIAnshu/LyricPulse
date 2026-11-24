using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;

namespace LyricPulse.Core.Lyrics;

/// <summary>
/// Per-provider token-bucket rate limiter, 429 Retry-After handler,
/// transient exponential backoff retryer, and cooldown circuit breaker.
/// </summary>
public sealed partial class ProviderHttpResilience : IDisposable
{
    private static readonly TimeSpan PerRequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxInlineRetryAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxCooldownDuration = TimeSpan.FromSeconds(60);
    private static readonly int[] BaseBackoffMs = [250, 750];

    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly ILogger _logger;
    private readonly string _providerName;

    private long _cooldownUntilUtcTicks;
    private bool _disposed;

    public ProviderHttpResilience(
        string providerName,
        TokenBucketRateLimiterOptions rateLimiterOptions,
        ILogger logger,
        Func<DateTimeOffset>? utcNowProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _providerName = providerName;
        _rateLimiter = new TokenBucketRateLimiter(rateLimiterOptions);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _utcNowProvider = utcNowProvider ?? (static () => DateTimeOffset.UtcNow);
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public bool IsInCooldown => _utcNowProvider().UtcTicks < Volatile.Read(ref _cooldownUntilUtcTicks);

    public async Task<HttpResponseMessage?> SendWithResilienceAsync(
        HttpClient httpClient,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsInCooldown)
        {
            _logger.LogProviderInCooldown(_providerName);
            return null;
        }

        for (var attempt = 0; attempt <= BaseBackoffMs.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var lease = await _rateLimiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
            if (!lease.IsAcquired)
            {
                _logger.LogProviderRateLimited(_providerName);
                return null;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(PerRequestTimeout);

            HttpResponseMessage? response = null;
            try
            {
                using var request = requestFactory();
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= BaseBackoffMs.Length)
                {
                    _logger.LogProviderTimedOut(_providerName);
                    return null;
                }

                await DelayBackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException ex)
            {
                if (attempt >= BaseBackoffMs.Length)
                {
                    _logger.LogProviderHttpFailed(ex, _providerName);
                    return null;
                }

                await DelayBackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryWait = ComputeRetryAfterWait(response);
                response.Dispose();

                if (retryWait > MaxInlineRetryAfter)
                {
                    TripCooldown(retryWait);
                    return null;
                }

                if (attempt >= BaseBackoffMs.Length)
                {
                    TripCooldown(retryWait);
                    return null;
                }

                await _delayAsync(retryWait, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (IsTransientStatus(response.StatusCode) && attempt < BaseBackoffMs.Length)
            {
                response.Dispose();
                await DelayBackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            return response;
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rateLimiter.Dispose();
    }

    private TimeSpan ComputeRetryAfterWait(HttpResponseMessage response)
    {
        TimeSpan baseWait = TimeSpan.FromSeconds(1);
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta.HasValue && retryAfter.Delta.Value > TimeSpan.Zero)
            {
                baseWait = retryAfter.Delta.Value;
            }
            else if (retryAfter.Date.HasValue)
            {
                var delta = retryAfter.Date.Value - _utcNowProvider();
                if (delta > TimeSpan.Zero)
                {
                    baseWait = delta;
                }
            }
        }

        var jitterMultiplier = 1.0 + (Random.Shared.NextDouble() * 0.30);
        return TimeSpan.FromTicks((long)(baseWait.Ticks * jitterMultiplier));
    }

    private void TripCooldown(TimeSpan requestedWait)
    {
        var clamped = requestedWait > MaxCooldownDuration ? MaxCooldownDuration : requestedWait;
        var until = _utcNowProvider() + clamped;
        Volatile.Write(ref _cooldownUntilUtcTicks, until.UtcTicks);
        _logger.LogProviderCooldownTripped(_providerName, clamped.TotalSeconds);
    }

    private Task DelayBackoffAsync(int attempt, CancellationToken ct)
    {
        var baseMs = BaseBackoffMs[Math.Min(attempt, BaseBackoffMs.Length - 1)];
        var jitter = 0.75 + (Random.Shared.NextDouble() * 0.50);
        return _delayAsync(TimeSpan.FromMilliseconds(baseMs * jitter), ct);
    }

    private static bool IsTransientStatus(HttpStatusCode code) =>
        code is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}
