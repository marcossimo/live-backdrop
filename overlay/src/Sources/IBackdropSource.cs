using Vortice.Direct3D11;

namespace LiveBackdrop.Sources;

/// <summary>Something that supplies the picture drawn behind the target window.</summary>
internal interface IBackdropSource : IDisposable
{
    /// <summary>Raised, possibly on a background thread, when a new picture is ready.</summary>
    event EventHandler? FrameReady;

    /// <summary>
    /// Gets the latest picture and where the target window's top-left corner falls in it,
    /// or false when there is none yet. Called with the target's visible frame.
    /// </summary>
    bool TryGetFrame(Native.Rect target, out ID3D11Texture2D? texture, out int x, out int y);
}
