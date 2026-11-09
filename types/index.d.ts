/** Where the backdrop's picture comes from. */
export type BackdropSource = 'wallpaper' | 'image'

/** Where the picture is blurred: nowhere, behind the app's text areas, or everywhere. */
export type BlurMode = 'off' | 'text' | 'everywhere'

/** Where the message colours come from: the ones chosen in the panel, or the wallpaper's. */
export type ColorMode = 'manual' | 'wallpaper'

/**
 * The user's settings, as `%LOCALAPPDATA%\LiveBackdrop\settings.json` holds them. The overlay
 * (LiveBackdrop.exe) reads the same file and applies every change within a frame.
 */
export type BackdropSettings = {
  enabled: boolean
  source: BackdropSource
  imagePath: string | null
  /** From 0 (invisible) to 1. */
  opacity: number
  startWithWindows: boolean
  blur: BlurMode
  /** Blur strength in pixels (Gaussian standard deviation), from 0 to 40. */
  blurAmount: number
  /** Leaves embedded web pages (the built-in browser, previews) without a backdrop. */
  clearWebPages: boolean
  /** Background of the person's own messages, `#rrggbb`; null keeps the app's. */
  userBubbleColor: string | null
  /** Text colour of the person's own messages, `#rrggbb`; null keeps the app's. */
  userTextColor: string | null
  /** Background behind Claude's replies, `#rrggbb`; null for none. */
  replyBackgroundColor: string | null
  /** How opaque that background is, from 0 to 1. */
  replyBackgroundOpacity: number
  /** `wallpaper`: message colours follow the wallpaper (the overlay samples it). */
  colorMode: ColorMode
  /** The backdrop breathes, gently, while Claude is working. */
  reactToClaude: boolean
  /** The most frames a second the backdrop is redrawn, 0 to 60; 0 for the wallpaper's own rate. */
  maxFps: number
  /** Holds the picture still while the computer runs on battery. */
  pauseOnBattery: boolean
  /** Holds the picture still while another app has the focus. */
  pauseInBackground: boolean
  /** Shows the overlay's icon in the notification area. */
  trayIcon: boolean
  /** The key combination that turns the backdrop on and off, like `Ctrl+Alt+B`; empty for none. */
  hotkey: string
  /** Written by the overlay on every start: where to launch it from. */
  overlayPath: string | null
  /** Written by the overlay on every start: its version, like `0.4.0`. */
  overlayVersion: string | null
}

/** Colours the overlay took from the wallpaper (`wallpaper-colors.json`), as `#rrggbb`. */
export type WallpaperColors = {
  /** The wallpaper's most vivid colour, toned to work as a bubble. */
  accent: string
  /** White or black, whichever reads better on the accent. */
  text: string
  /** The wallpaper's overall tone. */
  average: string
}

/** The message colours in use, whichever mode they come from; null keeps the app's. */
export type MessageColors = {
  userBubbleColor: string | null
  userTextColor: string | null
  replyBackgroundColor: string | null
}

/** The settings that hold a colour. */
export type ColorSetting = 'userBubbleColor' | 'userTextColor' | 'replyBackgroundColor'

/** What the plugin knows about the overlay process. */
export type OverlayStatus = 'running' | 'stopped' | 'not-installed' | 'unsupported' | 'unknown'

declare module 'claude-code' {
  interface PluginState {
    'live-backdrop': {
      settings: BackdropSettings | null
      status: OverlayStatus
      /** A short line about work in progress or the last error, shown in the panel. */
      notice: string
      /** Which colour setting the panel shows a custom-colour field for, if any. */
      editingColor: ColorSetting | null
      /** The wallpaper's colours as the overlay last wrote them, or null. */
      wallpaperColors: WallpaperColors | null
      /** This plugin's version, from its manifest; the overlay it installs matches it. */
      version: string | null
    }
  }
}
