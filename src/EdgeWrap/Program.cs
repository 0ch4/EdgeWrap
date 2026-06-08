using System.Threading;
using EdgeWrap.UI;

namespace EdgeWrap;

internal static class Program
{
    private const string MutexName = @"Local\EdgeWrap.SingleInstance";
    private const string ShowEventName = @"Local\EdgeWrap.ShowSettings";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;

    [STAThread]
    private static void Main(string[] args)
    {
        bool silent = args.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase));

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance already owns the tray. Ask it to surface its settings window.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            return;
        }

        ApplicationConfiguration.Initialize();

        var tray = new TrayContext(startSilent: silent);

        // Future launches signal this event so the running instance opens its window.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => tray.RequestShowSettings(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        Application.Run(tray);

        _showEvent.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
