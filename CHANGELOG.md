# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.4.1] - 2026-11-09

### Changed

- About 85 MB less memory: the overlay's executable is no longer compressed, so .NET does not
  keep an unpacked copy of it in memory. The download stays about the same size; the program
  takes more disk space.
- The GPU memory the overlay keeps between frames (about half of it) goes back while the
  backdrop is off or Claude is minimized.
- The wallpaper is captured no faster than the backdrop is redrawn, on Windows 11 24H2 and
  later, so frames that would never be drawn are not taken.

### Fixed

- An unexpected error while looking for embedded web pages could close the overlay; it is now
  logged and the next look tries again.

## [0.4.0] - 2026-11-09

### Added

- One-step install: `/backdrop install` (or the panel's install button) downloads the overlay
  matching the plugin's version from the GitHub release, checks it against its SHA-256, unpacks
  it to `%LOCALAPPDATA%\Programs\LiveBackdrop` (or where it already runs), and starts it. The
  same button offers an update when the overlay is older than the plugin.
- Match the wallpaper: message colours can follow the wallpaper. The overlay samples the
  picture under the Claude window on the GPU every few seconds and writes its most vivid colour
  (toned for a bubble) and a readable text colour to `wallpaper-colors.json`; your bubble and
  the replies' background follow it as the wallpaper changes. New setting `colorMode`.
- Claude at work: the backdrop breathes, gently, while Claude is working, and settles when the
  turn ends. DirectComposition runs the animation, so it costs no extra frames. On by default;
  new setting `reactToClaude`.
- Notification area icon: a click turns the backdrop on or off; its menu has start with
  Windows, the settings folder, the log and Exit. New setting `trayIcon`.
- A global shortcut to turn the backdrop on and off, Ctrl+Alt+B by default. New setting
  `hotkey` (empty for none).
- Power: a frame-rate cap (`maxFps`), and the picture held still on battery (`pauseOnBattery`,
  on by default) or while another app has the focus (`pauseInBackground`). The wallpaper is no
  longer captured while the Claude window is hidden or minimized.
- An icon for the overlay's executable and for the plugin.

### Changed

- The overlay records its version in the settings (`overlayVersion`), shown in the panel.
- The panel applies each change to the settings as the file holds them, so a change made from
  the tray or the shortcut is never undone by the panel.
- A settings file caught half written is read again instead of counting as empty, and is
  never replaced while it cannot be read.

## [0.3.0] - 2026-11-08

### Changed

- The blur behind text now follows what the app actually draws: the overlay captures the
  Claude window and builds the mask from its letters and icons on the GPU, paragraph by
  paragraph. It no longer guesses the layout, so it works in new chats, with panels opening or
  closing and while scrolling, without margins to tune.
- UI Automation is used only to keep embedded web pages clear, and skips the conversation.
- Built against the Windows 11 24H2 SDK, so the Claude capture can be limited to 15 frames a
  second where Windows supports it.

## [0.2.1] - 2026-11-08

### Fixed

- In a new or almost empty chat, the whole conversation pane (and the space left by a closed
  side panel) was blurred; now nothing is, until there are messages to measure.
- A side panel caught halfway through opening or closing could stay blurred: a layout is now
  applied only once two scans agree, and scans run twice a second.

## [0.2.0] - 2026-11-08

### Added

- Blur, as frosted glass: behind the Claude window's text areas (side panels, toolbar,
  conversation column, found through UI Automation) or everywhere, with an adjustable
  strength. New settings `blur` and `blurAmount`, and a Blur section in the `/backdrop` panel.
  The blur fades in at the edges of the text areas instead of stopping at a hard line.
- Keep web pages clear: no backdrop over the built-in browser panel and other embedded web
  pages (setting `clearWebPages`, on by default).
- Colors: your messages' bubble and text colour and a background behind Claude's replies,
  each optional, in a new Colors section of the panel.

## [0.1.0] - 2026-11-08

### Added

- Overlay (`LiveBackdrop.exe`) showing the live desktop wallpaper, Wallpaper Engine's included,
  as a faint, click-through layer over the Claude desktop app, captured on the GPU at the
  wallpaper's own frame rate.
- Picture source: any image file, scaled to cover the window.
- Settings file shared by the overlay and the plugin, applied live.
- Claude Code plugin with `/backdrop` (settings panel), `/backdrop on` and `/backdrop off`.
- Opacity control, start with Windows.

[Unreleased]: https://github.com/marcossimo/live-backdrop/compare/v0.4.1...HEAD
[0.4.1]: https://github.com/marcossimo/live-backdrop/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/marcossimo/live-backdrop/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/marcossimo/live-backdrop/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/marcossimo/live-backdrop/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/marcossimo/live-backdrop/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/marcossimo/live-backdrop/releases/tag/v0.1.0
