using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using LyricPulse.Core.Abstractions;
using LyricPulse.Core.Models;

namespace LyricPulse.App.Platform;

/// <summary>
/// AOT-safe Win32 <c>RegisterHotKey</c> / <c>UnregisterHotKey</c> manager running on a dedicated
/// hidden <c>HWND_MESSAGE</c> pump thread (zero <c>WH_KEYBOARD_LL</c> hooks, zero timeout disposal races).
/// </summary>
public sealed class GlobalHotkeyManager : IGlobalHotkeyService
{
    private const uint WmAppProcessQueue = Win32Native.WM_APP + 1;
    private const uint WmAppShutdown = Win32Native.WM_APP + 2;
    private const int HotkeyIdOffset = 0x4C50; // "LP"

    private readonly ConcurrentQueue<HotkeyCommand> _commands = new();
    private readonly Dictionary<int, HotkeyAction> _registeredIds = new();
    private readonly ManualResetEventSlim _windowReady = new(false);
    private readonly Thread _messageThread;
    private readonly Action<Action>? _callbackDispatcher;

    private nint _messageHwnd;
    private bool _disposed;

    public GlobalHotkeyManager(Action<Action>? callbackDispatcher = null)
    {
        _callbackDispatcher = callbackDispatcher;
        _messageThread = new Thread(RunMessageLoop)
        {
            Name = "LyricPulse.GlobalHotkeyPump",
            IsBackground = true
        };
        _messageThread.Start();
        _windowReady.Wait(TimeSpan.FromSeconds(2));
    }

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public bool TryRegister(HotkeyAction action, string gesture, out string? errorMessage)
    {
        var hwnd = Volatile.Read(ref _messageHwnd);
        if (Volatile.Read(ref _disposed) || hwnd == 0)
        {
            errorMessage = "Global hotkey service is unavailable.";
            return false;
        }

        if (!HotkeyGestureParser.TryParse(gesture, out var parsed, out errorMessage))
        {
            return false;
        }

        // TaskCompletionSource never requires disposal, preventing ObjectDisposedException on timeout
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new HotkeyCommand(
            CommandKind: HotkeyCommandKind.Register,
            Action: action,
            Parsed: parsed,
            Completion: tcs);

        _commands.Enqueue(command);
        if (!Win32Native.PostMessage(hwnd, WmAppProcessQueue, 0, 0) ||
            !tcs.Task.Wait(TimeSpan.FromSeconds(2)))
        {
            errorMessage = $"Timed out registering shortcut '{parsed.NormalizedGesture}'.";
            return false;
        }

        errorMessage = command.ErrorMessage;
        return command.Succeeded;
    }

    /// <summary>
    /// Registers every hotkey mapping from <see cref="AppSettings.GlobalHotkeys"/> and returns
    /// any conflicts keyed by <see cref="HotkeyAction"/> (never throws).
    /// </summary>
    public IReadOnlyDictionary<HotkeyAction, string> RegisterAll(IReadOnlyDictionary<HotkeyAction, string> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        UnregisterAll();

        var conflicts = new Dictionary<HotkeyAction, string>();
        foreach (var (action, gesture) in bindings)
        {
            if (!TryRegister(action, gesture, out var err) && err is not null)
            {
                conflicts[action] = err;
            }
        }

        return conflicts;
    }

    public void UnregisterAll()
    {
        var hwnd = Volatile.Read(ref _messageHwnd);
        if (Volatile.Read(ref _disposed) || hwnd == 0)
        {
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new HotkeyCommand(
            CommandKind: HotkeyCommandKind.UnregisterAll,
            Action: default,
            Parsed: default,
            Completion: tcs);

        _commands.Enqueue(command);
        if (Win32Native.PostMessage(hwnd, WmAppProcessQueue, 0, 0))
        {
            tcs.Task.Wait(TimeSpan.FromSeconds(2));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
        {
            return;
        }

        var hwnd = Volatile.Read(ref _messageHwnd);
        if (hwnd != 0)
        {
            _ = Win32Native.PostMessage(hwnd, WmAppShutdown, 0, 0);
        }

        _messageThread.Join(TimeSpan.FromSeconds(1));
        _windowReady.Dispose();
    }

    private void RunMessageLoop()
    {
        var hwnd = Win32Native.CreateWindowEx(
            dwExStyle: 0,
            lpClassName: "STATIC",
            lpWindowName: "LyricPulseHotkeyHost",
            dwStyle: 0,
            x: 0,
            y: 0,
            nWidth: 0,
            nHeight: 0,
            hWndParent: Win32Native.HWND_MESSAGE,
            hMenu: 0,
            hInstance: 0,
            lpParam: 0);

        Volatile.Write(ref _messageHwnd, hwnd);
        _windowReady.Set();
        if (hwnd == 0)
        {
            return;
        }

        try
        {
            while (Win32Native.GetMessage(out var msg, hwnd, 0, 0) > 0)
            {
                if (msg.Message == WmAppShutdown)
                {
                    break;
                }

                if (msg.Message == WmAppProcessQueue)
                {
                    DrainCommandsOnMessageThread(hwnd);
                    continue;
                }

                if (msg.Message == Win32Native.WM_HOTKEY)
                {
                    var id = (int)msg.WParam;
                    if (_registeredIds.TryGetValue(id, out var action))
                    {
                        RaiseHotkeyPressed(action);
                    }

                    continue;
                }

                _ = Win32Native.TranslateMessage(ref msg);
                _ = Win32Native.DispatchMessage(ref msg);
            }
        }
        finally
        {
            UnregisterAllOnMessageThread(hwnd);
            _ = Win32Native.DestroyWindow(hwnd);
            Volatile.Write(ref _messageHwnd, 0);
        }
    }

    private void DrainCommandsOnMessageThread(nint hwnd)
    {
        while (_commands.TryDequeue(out var cmd))
        {
            try
            {
                if (cmd.CommandKind == HotkeyCommandKind.UnregisterAll)
                {
                    UnregisterAllOnMessageThread(hwnd);
                    cmd.Succeeded = true;
                }
                else
                {
                    var id = HotkeyIdOffset + (int)cmd.Action;
                    if (_registeredIds.ContainsKey(id))
                    {
                        _ = Win32Native.UnregisterHotKey(hwnd, id);
                        _registeredIds.Remove(id);
                    }

                    if (Win32Native.RegisterHotKey(hwnd, id, cmd.Parsed.Modifiers, cmd.Parsed.VirtualKey))
                    {
                        _registeredIds[id] = cmd.Action;
                        cmd.Succeeded = true;
                    }
                    else
                    {
                        var win32Err = Marshal.GetLastPInvokeError();
                        cmd.Succeeded = false;
                        cmd.ErrorMessage = win32Err == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
                            ? $"Shortcut '{cmd.Parsed.NormalizedGesture}' is already in use by another application."
                            : $"Failed to register shortcut '{cmd.Parsed.NormalizedGesture}' (Win32 error {win32Err}).";
                    }
                }
            }
            finally
            {
                cmd.Completion.TrySetResult(cmd.Succeeded);
            }
        }
    }

    private void UnregisterAllOnMessageThread(nint hwnd)
    {
        foreach (var id in _registeredIds.Keys)
        {
            _ = Win32Native.UnregisterHotKey(hwnd, id);
        }

        _registeredIds.Clear();
    }

    private void RaiseHotkeyPressed(HotkeyAction action)
    {
        var handler = HotkeyPressed;
        if (handler is null)
        {
            return;
        }

        if (_callbackDispatcher is not null)
        {
            _callbackDispatcher(() => handler(this, action));
        }
        else
        {
            handler(this, action);
        }
    }

    private enum HotkeyCommandKind
    {
        Register = 0,
        UnregisterAll = 1
    }

    private sealed class HotkeyCommand(
        HotkeyCommandKind CommandKind,
        HotkeyAction Action,
        ParsedHotkeyGesture Parsed,
        TaskCompletionSource<bool> Completion)
    {
        public HotkeyCommandKind CommandKind { get; } = CommandKind;
        public HotkeyAction Action { get; } = Action;
        public ParsedHotkeyGesture Parsed { get; } = Parsed;
        public TaskCompletionSource<bool> Completion { get; } = Completion;
        public bool Succeeded { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
