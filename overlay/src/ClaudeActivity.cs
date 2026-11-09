using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiveBackdrop;

/// <summary>
/// Whether Claude is working, as the plugin reports it: one small file per Claude Code session in
/// the <c>activity</c> folder beside the settings, <c>{"working": true}</c> from the start of a turn
/// (written again at each of its steps) until its end. Several sessions can run at once; Claude
/// is working while any of them is.
/// </summary>
internal static class ClaudeActivity
{
    /// <summary>
    /// A working file not written for this long is from a session that ended without saying so
    /// (closed mid-turn, crashed). Each step of a turn writes it again, so only a single tool call
    /// running longer than this reads as idle.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    // Files of sessions long gone are removed.
    private static readonly TimeSpan RemoveAfter = TimeSpan.FromDays(2);

    public static string FolderFor(string settingsPath) => Path.Combine(Path.GetDirectoryName(settingsPath)!, "activity");

    /// <summary>True when any session's file says it is working and was written recently.</summary>
    public static bool IsWorking(string folder, DateTime nowUtc)
    {
        if (!Directory.Exists(folder))
        {
            return false;
        }

        var working = false;
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.json"))
            {
                var age = nowUtc - file.LastWriteTimeUtc;
                if (age > RemoveAfter)
                {
                    TryDelete(file);
                    continue;
                }

                if (!working && age <= StaleAfter && SaysWorking(file.FullName))
                {
                    working = true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return working;
        }

        return working;
    }

    /// <summary>Reads one session's file; anything unreadable counts as not working.</summary>
    internal static bool SaysWorking(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) is JsonObject root
                && root["working"] is JsonValue value
                && value.TryGetValue(out bool working)
                && working;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
