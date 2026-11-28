using System.Diagnostics;
using Avalonia.Threading;

namespace LyricPulse.App.Platform;

/// <summary>
/// Single-instance Windows host guard backed by <c>Local\LyricPulse.SingleInstance</c> mutex
/// (with <see cref="AbandonedMutexException"/> recovery), <c>Local\LyricPulse.ShowWindow</c>,
/// and <c>Local\LyricPulse.StartDemo</c> <see cref="EventWaitHandle"/> handoffs (&lt;20ms wakeup).
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = @"Local\LyricPulse.SingleInstance";
    public const string DefaultShowWindowEventName = @"Local\LyricPulse.ShowWindow";
    public const string DefaultStartDemoEventName = @"Local\LyricPulse.StartDemo";
    private const double MinActivationIntervalMs = 200.0;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly EventWaitHandle _demoEvent;
    private readonly ManualResetEventSlim _shutdownEvent = new(false);
    private Thread? _listenerThread;
    private bool _ownsMutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle showEvent, EventWaitHandle demoEvent, bool ownsMutex)
    {
        _mutex = mutex;
        _showEvent = showEvent;
        _demoEvent = demoEvent;
        _ownsMutex = ownsMutex;
    }

    /// <summary>
    /// Attempts to become the primary process instance. Returns an owned guard on success,
    /// or signals the already-running instance (including <paramref name="requestDemoMode"/>)
    /// and returns <see langword="null"/>.
    /// </summary>
    public static SingleInstanceGuard? TryAcquirePrimaryOrSignalExisting(
        string mutexName = DefaultMutexName,
        string showWindowEventName = DefaultShowWindowEventName,
        bool requestDemoMode = false,
        string? startDemoEventName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(showWindowEventName);

        var demoEventName = string.IsNullOrWhiteSpace(startDemoEventName)
            ? $"{showWindowEventName}.Demo"
            : startDemoEventName;

        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: true, name: mutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            if (requestDemoMode)
            {
                SignalExistingInstance(demoEventName);
            }
            else
            {
                SignalExistingInstance(showWindowEventName);
            }

            return null;
        }

        if (!createdNew)
        {
            var acquiredAbandoned = false;
            try
            {
                acquiredAbandoned = mutex.WaitOne(0, exitContext: false);
            }
            catch (AbandonedMutexException)
            {
                acquiredAbandoned = true;
            }

            if (!acquiredAbandoned)
            {
                mutex.Dispose();
                if (requestDemoMode)
                {
                    SignalExistingInstance(demoEventName);
                }
                else
                {
                    SignalExistingInstance(showWindowEventName);
                }

                return null;
            }
        }

        EventWaitHandle showEvent;
        EventWaitHandle demoEvent;
        try
        {
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showWindowEventName);
            demoEvent = new EventWaitHandle(false, EventResetMode.AutoReset, demoEventName);
        }
        catch (UnauthorizedAccessException)
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceGuard(mutex, showEvent, demoEvent, ownsMutex: true);
    }

    public static bool SignalExistingInstance(string eventName = DefaultShowWindowEventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        try
        {
            if (!EventWaitHandle.TryOpenExisting(eventName, out var existingEvent))
            {
                return false;
            }

            using (existingEvent)
            {
                return existingEvent.Set();
            }
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void StartActivationListener(
        Action onShowRequested,
        Action<Action>? dispatchCallback = null,
        Action? onDemoRequested = null)
    {
        ArgumentNullException.ThrowIfNull(onShowRequested);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_listenerThread is not null)
        {
            return;
        }

        var dispatch = dispatchCallback ?? DefaultAvaloniaDispatch;
        var demoHandler = onDemoRequested ?? onShowRequested;

        _listenerThread = new Thread(() =>
        {
            WaitHandle[] handles = [_showEvent, _demoEvent, _shutdownEvent.WaitHandle];
            long lastDispatchedTs = 0;
            while (true)
            {
                var signaled = WaitHandle.WaitAny(handles);
                if (signaled == 2)
                {
                    break;
                }

                var now = Stopwatch.GetTimestamp();
                if (lastDispatchedTs != 0 && Stopwatch.GetElapsedTime(lastDispatchedTs, now).TotalMilliseconds < MinActivationIntervalMs)
                {
                    continue;
                }

                lastDispatchedTs = now;
                dispatch(signaled == 1 ? demoHandler : onShowRequested);
            }
        })
        {
            Name = "LyricPulse.SingleInstanceListener",
            IsBackground = true
        };

        _listenerThread.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdownEvent.Set();
        _listenerThread?.Join(TimeSpan.FromMilliseconds(500));
        _showEvent.Dispose();
        _demoEvent.Dispose();
        _shutdownEvent.Dispose();

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Safe if shutdown runs on a different thread than Main.
            }

            _ownsMutex = false;
        }

        _mutex.Dispose();
    }

    private static void DefaultAvaloniaDispatch(Action callback) =>
        Dispatcher.UIThread.Post(callback, DispatcherPriority.Normal);
}
