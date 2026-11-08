using System.Diagnostics;

namespace LiveBackdrop;

/// <summary>Finds the window the backdrop sits on: the Claude desktop app's main window.</summary>
internal static class TargetWindow
{
    private const string ProcessName = "claude";

    // The desktop app is installed as a Windows package; the same process name is also used
    // by the Claude Code CLI, which has no window of its own and must not match.
    private const string PackagedPathMarker = "WindowsApps";

    /// <summary>The largest visible top-level window of the Claude desktop app, or zero.</summary>
    public static IntPtr Find()
    {
        var processIds = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName?.Contains(PackagedPathMarker, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        processIds.Add((uint)process.Id);
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // A process we may not inspect is not ours to follow.
                }
                catch (InvalidOperationException)
                {
                    // It exited meanwhile.
                }
            }
        }

        if (processIds.Count == 0)
        {
            return IntPtr.Zero;
        }

        var best = IntPtr.Zero;
        long bestArea = 0;
        Native.EnumWindows(
            (hwnd, _) =>
            {
                Native.GetWindowThreadProcessId(hwnd, out var processId);
                if (processIds.Contains(processId) && Native.IsWindowVisible(hwnd))
                {
                    Native.GetWindowRect(hwnd, out var rect);
                    var area = (long)rect.Width * rect.Height;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = hwnd;
                    }
                }

                return true;
            },
            IntPtr.Zero);
        return best;
    }
}
