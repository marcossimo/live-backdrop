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
        var version = typeof(SettingsStore).Assembly.GetName().Version?.ToString(3);
        Update(root =>
        {
            if (root["overlayPath"]?.GetValue<string>() == exePath && root["version"] is not null
                && root["overlayVersion"]?.GetValue<string>() == version)
            {
                return false;
            }

            root["version"] ??= Settings.CurrentVersion;
            root["overlayPath"] = exePath;
            root["overlayVersion"] = version;
            return true;
        });
    }

    /// <summary>Turns the backdrop on or off, as the tray menu and the hotkey do.</summary>
    public void SetEnabled(bool enabled) => SetField("enabled", enabled);

    /// <summary>Adds or removes the overlay from the sign-in list, as the tray menu does.</summary>
    public void SetStartWithWindows(bool enabled) => SetField("startWithWindows", enabled);

    /// <summary>
    /// Reads the file, lets <paramref name="change"/> edit its JSON (returning false to leave it
    /// as it is) and writes it back atomically, keeping every field the change did not touch.
    /// A file that exists but cannot be read is left alone rather than replaced.
    /// </summary>
    public void Update(Func<JsonObject, bool> change)
    {
        JsonObject root;
        try
        {
            root = File.Exists(FilePath) ? JsonNode.Parse(ReadShared()) as JsonObject ?? new JsonObject() : new JsonObject();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log.Write($"settings not updated: {ex.Message}");
            return;
        }

        if (change(root))
        {
            WriteAtomically(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private void SetField(string name, bool value) => Update(root =>
    {
        if (root[name] is JsonValue current && current.TryGetValue(out bool existing) && existing == value)
        {
            return false;
        }

        root[name] = value;
        return true;
    });

    // The plugin may be mid-write; a few short retries ride that out.
    private string ReadShared()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(FilePath);
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(30);
            }
        }
    }

    public void Dispose() => watcher?.Dispose();

    private Settings Load()
    {
        try
        {
            return File.Exists(FilePath) ? Settings.Parse(ReadShared()) : new Settings();
        }
        catch (IOException)
        {
            return Current ?? new Settings();
        }
    }

    private void WriteAtomically(string text)
    {
        var temp = FilePath + ".overlay.tmp";
        try
        {
            File.WriteAllText(temp, text);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"settings not written: {ex.Message}");
        }
    }
}
