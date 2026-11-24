using Avalonia.Threading;

namespace LyricPulse.App.Platform;

/// <summary>
/// Single-instance Windows host guard backed by <c>Local\LyricPulse.SingleInstance</c> mutex
/// (with <see cref="AbandonedMutexException"/> recovery) and <c>Local\LyricPulse.ShowWindow</c>
/// <see cref="EventWaitHandle"/> activation handoff (&lt;20ms second-launch wakeup).
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = @"Local\LyricPulse.SingleInstance";
    public const string DefaultShowWindowEventName = @"Local\LyricPulse.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly ManualResetEventSlim _shutdownEvent = new(false);
    private Thread? _listenerThread;
    private bool _ownsMutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle showEvent, bool ownsMutex)
    {
        _mutex = mutex;
        _showEvent = showEvent;
        _ownsMutex = ownsMutex;
    }

    /// <summary>
    /// Attempts to become the primary process instance. Returns an owned guard on success,
    /// or signals the already-running instance and returns <see langword="null"/>.
    /// </summary>
    public static SingleInstanceGuard? TryAcquirePrimaryOrSignalExisting(
        string mutexName = DefaultMutexName,
        string showWindowEventName = DefaultShowWindowEventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(showWindowEventName);

        var mutex = new Mutex(initiallyOwned: true, name: mutexName, out var createdNew);

        if (!createdNew)
        {
            var acquiredAbandoned = false;
            try
            {
                acquiredAbandoned = mutex.WaitOne(0, exitContext: false);
            }
            catch (AbandonedMutexException)
            {
                // Previous owner crashed abnormally; ownership now legally belongs to this process.
                acquiredAbandoned = true;
            }

            if (!acquiredAbandoned)
            {
                mutex.Dispose();
                SignalExistingInstance(showWindowEventName);
                return null;
            }
        }

        var showEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: showWindowEventName);

        return new SingleInstanceGuard(mutex, showEvent, ownsMutex: true);
    }

    public static bool SignalExistingInstance(string showWindowEventName = DefaultShowWindowEventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(showWindowEventName);

        if (!EventWaitHandle.TryOpenExisting(showWindowEventName, out var existingEvent))
        {
            return false;
        }

        using (existingEvent)
        {
            return existingEvent.Set();
        }
    }

    public void StartActivationListener(
        Action onShowRequested,
        Action<Action>? dispatchCallback = null)
    {
        ArgumentNullException.ThrowIfNull(onShowRequested);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_listenerThread is not null)
        {
            return;
        }

        var dispatch = dispatchCallback ?? DefaultAvaloniaDispatch;
        _listenerThread = new Thread(() =>
        {
            WaitHandle[] handles = [_showEvent, _shutdownEvent.WaitHandle];
            while (true)
            {
                var signaled = WaitHandle.WaitAny(handles);
                if (signaled == 1)
                {
                    break;
                }

                dispatch(onShowRequested);
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
