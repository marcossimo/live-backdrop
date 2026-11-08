using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiveBackdrop;

/// <summary>
/// Reads <c>%LOCALAPPDATA%\LiveBackdrop\settings.json</c> and raises <see cref="Changed"/> when
/// the file changes, so edits from the Claude Code plugin apply without a restart.
/// </summary>
internal sealed class SettingsStore : IDisposable
{
    private readonly FileSystemWatcher? watcher;

    public SettingsStore(string path)
    {
        FilePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Current = Load();
        try
        {
            watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            watcher.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            watcher.Created += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            watcher.Renamed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (IOException ex)
        {
            Log.Write($"settings watcher unavailable: {ex.Message}");
        }
    }

    /// <summary>Raised on a background thread when the file may have changed.</summary>
    public event EventHandler? Changed;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveBackdrop", "settings.json");

    public string FilePath { get; }

    public Settings Current { get; private set; }

    /// <summary>Reads the file again; returns true when the settings differ from before.</summary>
    public bool Reload()
    {
        var next = Load();
        if (next == Current)
        {
            return false;
        }

        Current = next;
        return true;
    }

    /// <summary>
    /// Records where this executable lives (so the plugin can start it) and fills in the format
    /// version, keeping every other field as the user or the plugin wrote it.
    /// </summary>
    public void RecordOverlayPath(string exePath)
    {
        JsonObject root;
        try
        {
            root = File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject() : new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        if (root["overlayPath"]?.GetValue<string>() == exePath && root["version"] is not null)
        {
            return;
        }

        root["version"] ??= Settings.CurrentVersion;
        root["overlayPath"] = exePath;
        WriteAtomically(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose() => watcher?.Dispose();

    private Settings Load()
    {
        // The plugin may be mid-write; a few short retries ride that out.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return File.Exists(FilePath) ? Settings.Parse(File.ReadAllText(FilePath)) : new Settings();
            }
            catch (IOException)
            {
                Thread.Sleep(30);
            }
        }

        return Current ?? new Settings();
    }

    private void WriteAtomically(string text)
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, FilePath, overwrite: true);
    }
}
