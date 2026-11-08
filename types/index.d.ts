/** Where the backdrop's picture comes from. */
export type BackdropSource = 'wallpaper' | 'image'

/** Where the picture is blurred: nowhere, behind the app's text areas, or everywhere. */
export type BlurMode = 'off' | 'text' | 'everywhere'

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
  /** Written by the overlay on every start: where to launch it from. */
  overlayPath: string | null
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
    }
  }
}
