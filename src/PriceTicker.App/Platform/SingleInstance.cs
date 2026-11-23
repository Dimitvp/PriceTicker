using System.Runtime.InteropServices;

namespace PriceTicker.App.Platform;

/// <summary>
/// Keeps one copy of the app per Windows session. A second launch (a double-click on the exe,
/// or the autostart while it already runs) asks the first copy to show its window and exits.
/// On other systems every launch runs on its own.
/// </summary>
internal sealed partial class SingleInstance : IDisposable
{
    private const string DefaultName = "PriceTicker-7f3c2e91";

    // Lets the running copy take the foreground: Windows only allows that to the process the user just started.
    private const int AnyProcess = -1;

    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _showRequested;
    private readonly ManualResetEvent _stopping = new(initialState: false);
    private Thread? _listener;

    private SingleInstance(Mutex? mutex, EventWaitHandle? showRequested, bool isFirst)
    {
        _mutex = mutex;
        _showRequested = showRequested;
        IsFirst = isFirst;
    }

    /// <summary>Raised on a background thread when another launch asked this copy to show itself.</summary>
    public event EventHandler? ShowRequested;

    /// <summary>False when another copy already runs; it has been asked to show its window.</summary>
    public bool IsFirst { get; }

    /// <param name="name">Overridable so tests do not collide with a running app.</param>
    public static SingleInstance Acquire(string name = DefaultName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new SingleInstance(mutex: null, showRequested: null, isFirst: true);
        }

        // "Local\" is per Windows session: another user signed in on the same PC gets their own copy.
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{name}", out var createdNew);
        var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}-show");

        if (createdNew)
        {
            return new SingleInstance(mutex, showRequested, isFirst: true);
        }

        _ = AllowSetForegroundWindow(AnyProcess);
        showRequested.Set();
        showRequested.Dispose();
        mutex.Dispose();
        return new SingleInstance(mutex: null, showRequested: null, isFirst: false);
    }

    /// <summary>Starts waiting for show requests from later launches. Call once, on the first copy.</summary>
    public void Listen()
    {
        if (_showRequested is null || _listener is not null)
        {
            return;
        }

        _listener = new Thread(WaitForRequests) { IsBackground = true, Name = "SingleInstance" };
        _listener.Start();
    }

    public void Dispose()
    {
        _stopping.Set();
        _listener?.Join(TimeSpan.FromSeconds(1));
        _showRequested?.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }

        _stopping.Dispose();
    }

    private void WaitForRequests()
    {
        WaitHandle[] handles = [_showRequested!, _stopping];
        while (WaitHandle.WaitAny(handles) == 0)
        {
            ShowRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
