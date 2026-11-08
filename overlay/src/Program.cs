namespace LiveBackdrop;

internal static class Program
{
    // One overlay per user session; a second launch just exits.
    private const string SingleInstanceName = @"Local\LiveBackdrop.SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        var settingsPath = SettingsStore.DefaultPath;
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--settings")
            {
                settingsPath = Path.GetFullPath(args[i + 1]);
            }
        }

        Log.FilePath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "overlay.log");

        using var single = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirst);
        if (!isFirst)
        {
            return 0;
        }

        try
        {
            using var settings = new SettingsStore(settingsPath);
            settings.RecordOverlayPath(Environment.ProcessPath!);
            Log.Write($"started {typeof(Program).Assembly.GetName().Version} with {settingsPath}");

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.Run(new OverlayWindow(settings));
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write($"fatal: {ex}");
            return 1;
        }
    }
}
