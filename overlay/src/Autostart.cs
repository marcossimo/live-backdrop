using Microsoft.Win32;

namespace LiveBackdrop;

/// <summary>Adds or removes the overlay from the current user's "run at sign-in" list.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LiveBackdrop";

    public static void Apply(bool enabled, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            var command = $"\"{exePath}\"";
            if (enabled)
            {
                if (key.GetValue(ValueName) as string != command)
                {
                    key.SetValue(ValueName, command);
                    Log.Write("autostart on");
                }
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Write("autostart off");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Write($"autostart unchanged: {ex.Message}");
        }
    }
}
