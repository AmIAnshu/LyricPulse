using LyricPulse.Core.Abstractions;

namespace LyricPulse.App.Platform;

/// <summary>
/// Fullscreen exclusive D3D game and presentation detector backed by
/// <c>SHQueryUserNotificationState</c> polling on a clean 1-second timer.
/// </summary>
public sealed class FullscreenAutoHideMonitor : IFullscreenDetector, IDisposable
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    private readonly Func<bool> _probeFunc;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private bool _lastState;
    private bool _disposed;

    public FullscreenAutoHideMonitor(
        TimeSpan? pollInterval = null,
        Func<bool>? customProbe = null)
    {
        _pollInterval = pollInterval is { TotalMilliseconds: >= 10 } valid ? valid : DefaultPollInterval;
        _probeFunc = customProbe ?? QueryWindowsFullscreenState;
    }

    public bool IsCurrentlyFullscreen => Volatile.Read(ref _lastState);

    public event EventHandler<bool>? FullscreenStateChanged;

    public bool IsFullscreenExclusiveOrPresentation()
    {
        if (_disposed)
        {
            return false;
        }

        return _probeFunc();
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pollTask is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _pollTask = Task.Run(() => PollLoopAsync(token), token);
    }

    public void Stop()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _pollTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(static inner => inner is OperationCanceledException))
        {
            // Expected on cancellation.
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _pollTask = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    internal static bool IsFullscreenState(QueryUserNotificationState state) =>
        state is QueryUserNotificationState.Busy
            or QueryUserNotificationState.RunningD3DFullScreen
            or QueryUserNotificationState.PresentationMode;

    private static bool QueryWindowsFullscreenState()
    {
        var hr = Win32Native.SHQueryUserNotificationState(out var state);
        return hr == 0 && IsFullscreenState(state);
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        EvaluateAndNotify();

        using var timer = new PeriodicTimer(_pollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                EvaluateAndNotify();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void EvaluateAndNotify()
    {
        var current = _probeFunc();
        var previous = Volatile.Read(ref _lastState);
        if (current != previous)
        {
            Volatile.Write(ref _lastState, current);
            FullscreenStateChanged?.Invoke(this, current);
        }
    }
}
