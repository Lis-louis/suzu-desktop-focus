using Suzu.App;

namespace Suzu;

internal static class Program
{
    /// <summary>二重起動の防止。インストーラー（Inno Setup の AppMutex）も同じ名前で起動中かを確認する。</summary>
    public const string MutexName = "SuzuDesktopMascot";
    private const string ReopenEventName = "SuzuDesktopMascot.Reopen";

    [STAThread]
    private static int Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // すでに起動している鈴にコントロールパネルを開いてもらう
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ReopenEventName);
                ev.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool autostart = args.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        bool updated = args.Any(a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase));
        AppController controller;
        try { controller = new AppController(autostart, updated); }
        catch (Exception) { return 1; }

        using var reopen = new EventWaitHandle(false, EventResetMode.AutoReset, ReopenEventName);
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var registration = ThreadPool.RegisterWaitForSingleObject(reopen,
            (_, _) => ui.Post(_ => controller.OnReopened(), null), null, Timeout.Infinite, executeOnlyOnce: false);

        Application.ApplicationExit += (_, _) => controller.SaveOnExit();
        Application.Run(controller);
        registration.Unregister(null);
        return 0;
    }
}
