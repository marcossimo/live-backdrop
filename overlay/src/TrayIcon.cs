using System.Diagnostics;

namespace LiveBackdrop;

/// <summary>
/// The overlay's icon in the notification area: a click turns the backdrop on or off, and the
/// menu adds start with Windows, the settings folder, the log and Exit. Changes are written to
/// the settings file, so the plugin's panel and the overlay stay in step.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly SettingsStore settings;
    private readonly NotifyIcon icon;
    private readonly Icon onIcon;
    private readonly Icon offIcon;
    private readonly ToolStripMenuItem enabledItem;
    private readonly ToolStripMenuItem autostartItem;

    public TrayIcon(SettingsStore settings, Action exit)
    {
        this.settings = settings;
        onIcon = LoadIcon("LiveBackdrop.ico");
        offIcon = LoadIcon("LiveBackdropOff.ico");

        enabledItem = new ToolStripMenuItem("Backdrop on", null, (_, _) => Toggle());
        autostartItem = new ToolStripMenuItem("Start with Windows", null,
            (_, _) => settings.SetStartWithWindows(!settings.Current.StartWithWindows));
        var folder = Path.GetDirectoryName(settings.FilePath)!;
        var version = typeof(TrayIcon).Assembly.GetName().Version?.ToString(3);
        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            new ToolStripMenuItem($"Live Backdrop {version}") { Enabled = false },
            new ToolStripSeparator(),
            enabledItem,
            autostartItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Open settings folder", null, (_, _) => Open(folder)),
            new ToolStripMenuItem("Open log", null, (_, _) => Open(Log.FilePath)),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => exit()),
        ]);

        icon = new NotifyIcon { ContextMenuStrip = menu };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Toggle();
            }
        };
    }

    /// <summary>Shows the current settings: on or off, the hotkey; hides the icon when not wanted.</summary>
    public void Update(Settings current, string? hotkey)
    {
        enabledItem.Checked = current.Enabled;
        autostartItem.Checked = current.StartWithWindows;
        icon.Icon = current.Enabled ? onIcon : offIcon;

        // The tooltip holds at most 127 characters.
        var text = $"Live Backdrop: {(current.Enabled ? "on" : "off")}";
        if (hotkey is not null)
        {
            text += $" ({hotkey})";
        }

        icon.Text = text.Length > 127 ? text[..127] : text;
        icon.Visible = current.TrayIcon;
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.ContextMenuStrip?.Dispose();
        icon.Dispose();
        onIcon.Dispose();
        offIcon.Dispose();
    }

    private void Toggle() => settings.SetEnabled(!settings.Current.Enabled);

    private static Icon LoadIcon(string name)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream($"LiveBackdrop.Assets.{name}")
            ?? throw new InvalidOperationException($"Missing resource {name}.");

        // The notification area draws the small size; ask for it so Windows does not scale the large one.
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    private static void Open(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Path.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Write($"could not open {path}: {ex.Message}");
        }
    }
}
