# Architecture

Live Backdrop has two parts that talk through one file.

```
Claude Code plugin (TypeScript)          Overlay (C#, LiveBackdrop.exe)
  /backdrop panel                          follows the Claude window
  writes settings.json  ───────────────▶   watches settings.json
  starts the overlay                       captures the wallpaper, draws the layer
```

`%LOCALAPPDATA%\LiveBackdrop\settings.json`:

```json
{
  "version": 1,
  "enabled": true,
  "source": "wallpaper",
  "imagePath": null,
  "opacity": 0.18,
  "startWithWindows": false,
  "blur": "text",
  "blurAmount": 12,
  "clearWebPages": true,
  "userBubbleColor": "#2563eb",
  "userTextColor": "#ffffff",
  "replyBackgroundColor": "#ffffff",
  "replyBackgroundOpacity": 0.08,
  "overlayPath": "C:\\Users\\you\\AppData\\Local\\Programs\\LiveBackdrop\\LiveBackdrop.exe"
}
```

Both sides read it leniently: unknown fields are kept and ignored, a missing or invalid field
takes its default. The plugin writes the fields it owns over what is there; the overlay writes
only `version` and `overlayPath`. When a change to the format cannot be read by older versions,
raise `version` and handle the old shape in both readers.

## The plugin (`hooks/`, `types/`, `tests/`)

| File | Role |
| --- | --- |
| `hooks/register.tsx` | Registers `/backdrop` and the panel's render hook |
| `hooks/panel.tsx` | The settings panel, in sections. New settings go here |
| `hooks/messages.tsx` | Message colours: the person's bubbles and the background of replies |
| `hooks/state.ts` | The panel's state (settings, overlay status, notice), refresh and apply |
| `hooks/settings.ts` | Defaults, lenient parsing, merging, reading and writing the file |
| `hooks/overlay.ts` | Whether the overlay runs, and starting it |
| `types/index.d.ts` | The settings type and the plugin's state contract |

The panel uses the elements every surface offers (Box, Text, Button) and adds Select and Input
where the surface has them.

## The overlay (`overlay/src/`)

| File | Role |
| --- | --- |
| `Program.cs` | Single instance, settings path, startup |
| `OverlayWindow.cs` | The click-through window; follows Claude, applies settings |
| `Compositor.cs` | Direct3D 11 device, composition swap chain, DirectComposition visual and opacity |
| `BlurRenderer.cs` | The blur, drawn with Direct2D: everywhere, or through a mask built from the app's own picture |
| `Capture/WindowCapture.cs` | Windows Graphics Capture of one window (the desktop host, the Claude window) |
| `Sources/WallpaperSource.cs` | Windows Graphics Capture of the desktop host window |
| `Sources/ImageSource.cs` | A picture file, scaled to cover the window |
| `TargetWindow.cs` | Finds the Claude desktop app's main window |
| `WebPageScanner.cs` | Finds the Claude window's embedded web pages, to keep them clear |
| `RectMath.cs` | Rectangle intersection and subtraction |
| `Settings.cs`, `SettingsStore.cs` | The settings model, parsing and file watching |
| `Autostart.cs` | The current user's "run at sign-in" entry |
| `Native.cs` | Win32, DWM and WinRT interop |

### The window

The overlay window has the extended styles `WS_EX_NOREDIRECTIONBITMAP` (DirectComposition draws
it), `WS_EX_LAYERED | WS_EX_TRANSPARENT` (mouse input passes through to the window below),
`WS_EX_TOOLWINDOW` (no taskbar button) and `WS_EX_NOACTIVATE` (never takes the focus). Its owner
is set to the Claude window, so Windows keeps it just above Claude in the z-order and hides it
when Claude is minimized. A 15 ms timer follows Claude's visible frame (DWM extended frame
bounds).

### The wallpaper

On Windows 10 and 11 the wallpaper, Wallpaper Engine's included, is drawn inside the top-level
`Progman` ("Program Manager") window, which spans every monitor. Windows Graphics Capture of that
window delivers its content on the GPU even where other windows cover it, without the desktop
icons, at the rate the wallpaper redraws. Each frame is copied to a texture we keep, so moving
the Claude window can redraw from the last frame while the wallpaper stands still. The part
under Claude is copied into a composition swap chain the size of the Claude window.

### The blur

The blur is drawn into the frame itself, by `BlurRenderer` with Direct2D on the same device:
the window's part of the wallpaper is drawn sharp, and a Gaussian blur of it is drawn on top
through an alpha mask. *Everywhere* draws the blurred copy alone. Blurs use hard border mode,
so the window's edges stay blurred.

The mask comes from the app's own picture. `OverlayWindow` also captures the Claude window
(`WindowCapture`; the capture never includes our layer, which is a window of its own), and the
mask is built from it, with Direct2D effects on the GPU:

1. edge detection (Sobel) at full size: letters, icons and buttons are edges, the app's flat
   background is not;
2. a colour matrix turns edge strength into opacity, leaving faint edges (gradients,
   anti-aliasing) out;
3. the result is drawn at half size, then grown (morphology, dilate) so the letters of a
   paragraph merge into one block, and its opacity is restored after the shrink;
4. a Gaussian blur softens the blocks' borders (`FeatherFor`), and the mask is drawn back at
   full size.

Shrinking before the edge detection blurs small letters below the threshold, which is why it
comes after. Direct2D keeps an effect's result until its input is set again, so inputs are set
again whenever the bitmaps behind them are redrawn. The mask is rebuilt only when the app drew
a new picture, at most 15 times a second; frames from both captures only mark the picture
stale, and the follow timer redraws it at most every 30 ms. `LIVEBACKDROP_DUMP=<folder>` saves
the mask's stages as PNGs once, for tuning.

An earlier version guessed where the text is from the app's accessibility tree (side panels,
toolbar, the conversation column measured from its messages) and blurred rectangles there. It
broke on every new layout (an empty chat, a panel caught mid-animation, a command's output row),
needed margins, lagged behind scrolling and walked the app's tree twice a second.

`Compositor` shows the swap chain through a small DirectComposition tree. The root visual
carries the opacity. With nothing to keep clear, it shows the swap chain itself; with web
pages to keep clear, the root shows nothing and "pieces" do: the window minus those areas
(`RectMath.Subtract`), each a visual clipped to its area holding a visual that shows the swap
chain. Use an `IDCompositionRectangleClip`; Vortice's `SetClip(RawRectF)` overload did not
reach Windows correctly.

### Web pages

`WebPageScanner` walks the Claude window's UI Automation tree (through FlaUI and the COM API,
which exposes ARIA roles; the managed `System.Windows.Automation` API does not) on its own
thread every second, and right after a resize, skipping the parts that never hold a page (the
conversation, side panels, toolbars). An embedded web page is a `document` smaller than the
window (the app itself is one filling it), shown only if UI Automation hits it at its own
centre (a closed browser leaves its page in the tree); what is kept clear is the smallest app
`region` strictly around it, so the browser's address bar is included. A result is applied
once two scans in a row agree, so a panel caught mid-animation is never used.

### Message colours

Unlike the backdrop, message colours are not drawn by the overlay: the plugin's `ui.render`
hooks for `UserMessage` and `AssistantMessage` give the app a tree to draw instead of its own,
so colours scroll with the chat and text stays crisp. A reply is wrapped: the hook returns a
coloured `Box` around `await next(e)`, the app's own drawing, so code blocks and links keep
working; the app's markdown cannot take a text colour. The person's message is redrawn whole
(a `Text` with the chosen colour in a bubble aligned right), because the app's own bubble would
otherwise show inside ours; that drawing has no attachments. `borderStyle="round"` in the
bubble's colour is what rounds its corners on the desktop. Prompts sent from the desktop app
arrive with the origin `sdk`, so the hook tells the person's prompts apart by the absence of
`task` and `from` instead.

### Ruled out

| Idea | Why not |
| --- | --- |
| A Claude Code plugin backdrop (`$.ui.backdrop`) | The plugin API has one, but the desktop app did not draw it at the time of writing. Worth revisiting: it would put the picture truly *behind* the text |
| Injecting CSS into the app | The app blocks remote debugging switches and validates its package; working around that would defeat its security |
| Making Claude's own window transparent | The app's window rejects the layered style |
| A colour tint over the text areas | Built and dropped: a tint over the window washes the app's text out instead of colouring the panels |
| DWM thumbnails of the desktop | They do not render inside layered windows, and a non-layered window cannot pass clicks through to another process |
| `PrintWindow` of the desktop at a timer | Works, but about 50 ms per frame on the CPU; capture on the GPU is cheaper and frame-accurate |
