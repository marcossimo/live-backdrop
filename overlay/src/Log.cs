namespace LiveBackdrop;

/// <summary>A small append-only log beside the settings file, for troubleshooting.</summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath { get; set; } = Path.Combine(Path.GetTempPath(), "LiveBackdrop.log");

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:s} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Logging must never take the overlay down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
