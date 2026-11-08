# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

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

[Unreleased]: https://github.com/marcossimo/live-backdrop/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/marcossimo/live-backdrop/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/marcossimo/live-backdrop/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/marcossimo/live-backdrop/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/marcossimo/live-backdrop/releases/tag/v0.1.0
