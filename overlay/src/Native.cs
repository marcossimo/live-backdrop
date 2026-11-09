using System.Runtime.InteropServices;

namespace LiveBackdrop;

/// <summary>The Win32, DWM and WinRT interop the overlay needs.</summary>
internal static partial class Native
{
    public const int GwlpHwndParent = -8;
    public const int SwHide = 0;
    public const int SwShowNoActivate = 4;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoOwnerZOrder = 0x0200;
    public const uint LwaAlpha = 0x2;
    public const int WmHotkey = 0x0312;
    public const uint GaRootOwner = 3;

    // Extended styles of the overlay window.
    public const int WsExNoRedirectionBitmap = 0x00200000; // DirectComposition draws it
    public const int WsExLayered = 0x00080000;             // with Transparent: clicks pass through
    public const int WsExTransparent = 0x00000020;
    public const int WsExToolWindow = 0x00000080;          // no taskbar button
    public const int WsExNoActivate = 0x08000000;          // never takes the focus

    private const int DwmwaExtendedFrameBounds = 9;

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindow(string className, IntPtr windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hwnd, int id);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);

    [LibraryImport("dcomp.dll")]
    public static partial int DCompositionCreateDevice3(IntPtr renderingDevice, in Guid iid, out IntPtr device);

    [LibraryImport("d3d11.dll")]
    public static partial int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WindowsCreateString(string source, int length, out IntPtr hstring);

    [LibraryImport("combase.dll")]
    public static partial int WindowsDeleteString(IntPtr hstring);

    [LibraryImport("combase.dll")]
    public static partial int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

    /// <summary>The visible frame of a window, without the invisible resize border.</summary>
    public static Rect VisibleFrame(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<Rect>()) != 0)
        {
            GetWindowRect(hwnd, out rect);
        }

        return rect;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;

        public int Height => Bottom - Top;
    }
}
