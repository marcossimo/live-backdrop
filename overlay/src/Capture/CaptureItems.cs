using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace LiveBackdrop.Capture;

/// <summary>The COM interop Windows Graphics Capture needs from a desktop app.</summary>
internal static class CaptureItems
{
    private const string CaptureItemClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
    private static readonly Guid CaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, in Guid iid);

        IntPtr CreateForMonitor(IntPtr monitor, in Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface(in Guid iid);
    }

    public static GraphicsCaptureItem ForWindow(IntPtr hwnd)
    {
        Marshal.ThrowExceptionForHR(Native.WindowsCreateString(CaptureItemClass, CaptureItemClass.Length, out var className));
        IntPtr factoryPointer;
        try
        {
            Marshal.ThrowExceptionForHR(Native.RoGetActivationFactory(className, typeof(IGraphicsCaptureItemInterop).GUID, out factoryPointer));
        }
        finally
        {
            Native.WindowsDeleteString(className);
        }

        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPointer);
        Marshal.Release(factoryPointer);
        var itemPointer = interop.CreateForWindow(hwnd, CaptureItemIid);
        try
        {
            return GraphicsCaptureItem.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    public static ID3D11Texture2D Texture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        return new ID3D11Texture2D(access.GetInterface(typeof(ID3D11Texture2D).GUID));
    }
}
