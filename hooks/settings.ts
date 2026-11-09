import type { EngineInterface } from 'claude-code'

import type { BackdropSettings, BlurMode, MessageColors, WallpaperColors } from '../types'

/** The settings file format version this plugin writes. Keep in step with the overlay. */
export const SETTINGS_VERSION = 1

export const DEFAULT_OPACITY = 0.18

export const DEFAULT_BLUR_AMOUNT = 12

export const MAX_BLUR_AMOUNT = 40

const BLUR_MODES: readonly BlurMode[] = ['off', 'text', 'everywhere']

export const DEFAULT_REPLY_BACKGROUND_OPACITY = 0.08

export const MAX_FPS = 60

export const DEFAULT_HOTKEY = 'Ctrl+Alt+B'

const HEX_COLOR = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i

export const DEFAULTS: BackdropSettings = {
  enabled: true,
  source: 'wallpaper',
  imagePath: null,
  opacity: DEFAULT_OPACITY,
  startWithWindows: false,
  blur: 'text',
  blurAmount: DEFAULT_BLUR_AMOUNT,
  clearWebPages: true,
  userBubbleColor: null,
  userTextColor: null,
  replyBackgroundColor: null,
  replyBackgroundOpacity: DEFAULT_REPLY_BACKGROUND_OPACITY,
  colorMode: 'manual',
  reactToClaude: true,
  maxFps: 0,
  pauseOnBattery: true,
  pauseInBackground: false,
  trayIcon: true,
  hotkey: DEFAULT_HOTKEY,
  overlayPath: null,
  overlayVersion: null,
}

type JsonObject = Record<string, unknown>

function isObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** Rounds to whole percents and keeps the value within 0 to 1. */
export function clampOpacity(value: number): number {
  if (!Number.isFinite(value)) return DEFAULT_OPACITY
  return Math.round(Math.min(1, Math.max(0, value)) * 100) / 100
}

/** Rounds to whole pixels and keeps the value within 0 to {@link MAX_BLUR_AMOUNT}. */
export function clampBlurAmount(value: number): number {
  if (!Number.isFinite(value)) return DEFAULT_BLUR_AMOUNT
  return Math.round(Math.min(MAX_BLUR_AMOUNT, Math.max(0, value)))
}

/** Whole frames a second, within 0 (the wallpaper's own rate) to {@link MAX_FPS}. */
export function clampFps(value: number): number {
  if (!Number.isFinite(value)) return DEFAULTS.maxFps
  return Math.round(Math.min(MAX_FPS, Math.max(0, value)))
}

/** A colour as `#rgb` or `#rrggbb`, lowercased, or null for anything else. */
export function parseHexColor(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim().toLowerCase()
  return HEX_COLOR.test(trimmed) ? trimmed : null
}

/** `#rrggbb` (or `#rgb`) at the given opacity, as CSS `rgba()`. */
export function withOpacity(hex: string, opacity: number): string {
  const full = hex.length === 4 ? `#${[...hex.slice(1)].map(c => c + c).join('')}` : hex
  const [r, g, b] = [1, 3, 5].map(i => Number.parseInt(full.slice(i, i + 2), 16))
  return `rgba(${r}, ${g}, ${b}, ${clampOpacity(opacity)})`
}

/**
 * Reads settings leniently, as the overlay does: unknown fields are ignored and a missing or
 * invalid field takes its default.
 */
export function parseSettings(raw: unknown): BackdropSettings {
  const root = isObject(raw) ? raw : {}
  const text = (key: string): string | null => {
    const value = root[key]
    return typeof value === 'string' && value.trim() !== '' ? value : null
  }
  const fraction = (key: string, fallback: number): number => {
    const value = root[key]
    return typeof value === 'number' ? clampOpacity(value) : fallback
  }
  const flag = (key: string, fallback: boolean): boolean => {
    const value = root[key]
    return typeof value === 'boolean' ? value : fallback
  }

  return {
    enabled: flag('enabled', DEFAULTS.enabled),
    source: root.source === 'image' ? 'image' : 'wallpaper',
    imagePath: text('imagePath'),
    opacity: typeof root.opacity === 'number' ? clampOpacity(root.opacity) : DEFAULTS.opacity,
    startWithWindows: flag('startWithWindows', DEFAULTS.startWithWindows),
    blur: BLUR_MODES.find(mode => mode === root.blur) ?? DEFAULTS.blur,
    blurAmount: typeof root.blurAmount === 'number' ? clampBlurAmount(root.blurAmount) : DEFAULTS.blurAmount,
    clearWebPages: flag('clearWebPages', DEFAULTS.clearWebPages),
    userBubbleColor: parseHexColor(root.userBubbleColor),
    userTextColor: parseHexColor(root.userTextColor),
    replyBackgroundColor: parseHexColor(root.replyBackgroundColor),
    replyBackgroundOpacity: fraction('replyBackgroundOpacity', DEFAULTS.replyBackgroundOpacity),
    colorMode: root.colorMode === 'wallpaper' ? 'wallpaper' : 'manual',
    reactToClaude: flag('reactToClaude', DEFAULTS.reactToClaude),
    maxFps: typeof root.maxFps === 'number' ? clampFps(root.maxFps) : DEFAULTS.maxFps,
    pauseOnBattery: flag('pauseOnBattery', DEFAULTS.pauseOnBattery),
    pauseInBackground: flag('pauseInBackground', DEFAULTS.pauseInBackground),
    trayIcon: flag('trayIcon', DEFAULTS.trayIcon),
    hotkey: typeof root.hotkey === 'string' ? root.hotkey.trim() : DEFAULTS.hotkey,
    overlayPath: text('overlayPath'),
    overlayVersion: text('overlayVersion'),
  }
}

/**
 * The file's new content: the fields this plugin owns, laid over what was there, so fields
 * written by the overlay (`overlayPath`, `overlayVersion`) or a newer version survive.
 */
export function mergeSettings(previous: unknown, next: BackdropSettings): JsonObject {
  const base = isObject(previous) ? previous : {}
  return {
    ...base,
    version: SETTINGS_VERSION,
    enabled: next.enabled,
    source: next.source,
    imagePath: next.imagePath,
    opacity: clampOpacity(next.opacity),
    startWithWindows: next.startWithWindows,
    blur: next.blur,
    blurAmount: clampBlurAmount(next.blurAmount),
    clearWebPages: next.clearWebPages,
    userBubbleColor: parseHexColor(next.userBubbleColor),
    userTextColor: parseHexColor(next.userTextColor),
    replyBackgroundColor: parseHexColor(next.replyBackgroundColor),
    replyBackgroundOpacity: clampOpacity(next.replyBackgroundOpacity),
    colorMode: next.colorMode === 'wallpaper' ? 'wallpaper' : 'manual',
    reactToClaude: next.reactToClaude,
    maxFps: clampFps(next.maxFps),
    pauseOnBattery: next.pauseOnBattery,
    pauseInBackground: next.pauseInBackground,
    trayIcon: next.trayIcon,
    hotkey: next.hotkey.trim(),
  }
}

/** Reads `wallpaper-colors.json` as the overlay writes it; null unless every colour is valid. */
export function parseWallpaperColors(raw: unknown): WallpaperColors | null {
  if (!isObject(raw)) return null
  const accent = parseHexColor(raw.accent)
  const text = parseHexColor(raw.text)
  const average = parseHexColor(raw.average)
  return accent && text && average ? { accent, text, average } : null
}

/**
 * The message colours to draw. In wallpaper mode, once the overlay has sampled it: your bubble
 * in the wallpaper's accent with readable text, and replies on the accent at the chosen opacity.
 * Otherwise, the colours chosen in the panel.
 */
export function messageColors(settings: BackdropSettings, wallpaper: WallpaperColors | null): MessageColors {
  if (settings.colorMode === 'wallpaper' && wallpaper) {
    return { userBubbleColor: wallpaper.accent, userTextColor: wallpaper.text, replyBackgroundColor: wallpaper.accent }
  }
  return {
    userBubbleColor: settings.userBubbleColor,
    userTextColor: settings.userTextColor,
    replyBackgroundColor: settings.replyBackgroundColor,
  }
}

/** `%LOCALAPPDATA%\LiveBackdrop`, the folder the plugin and the overlay share, or null off Windows. */
export async function dataFolder($: EngineInterface): Promise<string | null> {
  const localAppData = await $.env.get('LOCALAPPDATA')
  return localAppData ? `${localAppData}\\LiveBackdrop` : null
}

/** `%LOCALAPPDATA%\LiveBackdrop\settings.json`, or null off Windows. */
export async function settingsPath($: EngineInterface): Promise<string | null> {
  const folder = await dataFolder($)
  return folder ? `${folder}\\settings.json` : null
}

/** What a JSON file held: its value, nothing (no file), or unreadable (half written, say). */
type RawRead = { kind: 'value'; value: unknown } | { kind: 'missing' } | { kind: 'unreadable' }

// The overlay writes the settings too (from the tray and the hotkey); a read that lands
// mid-write is tried again a few times before it counts as unreadable.
async function readRaw($: EngineInterface, path: string): Promise<RawRead> {
  for (let attempt = 0; attempt < 4; attempt++) {
    if (attempt > 0) await $.clock.sleep(40)
    let text: string
    try {
      if (!(await $.fs.exists(path))) return { kind: 'missing' }
      text = String(await $.fs.read(path))
    } catch {
      continue
    }
    try {
      // PowerShell writes a byte order mark; JSON.parse refuses one.
      return { kind: 'value', value: JSON.parse(text.replace(/^﻿/, '')) }
    } catch {
      // Half written; try again.
    }
  }
  return { kind: 'unreadable' }
}

/** A JSON file's value, or null when it is missing or unreadable. */
export async function readJson($: EngineInterface, path: string): Promise<unknown> {
  const read = await readRaw($, path)
  return read.kind === 'value' ? read.value : null
}

export async function loadSettings($: EngineInterface): Promise<BackdropSettings> {
  const path = await settingsPath($)
  if (!path) return DEFAULTS
  // Missing or unreadable, every field takes its default.
  const read = await readRaw($, path)
  return parseSettings(read.kind === 'value' ? read.value : {})
}

/**
 * Saves new settings over what the file holds now. A file that exists but cannot be read is
 * not replaced: that would lose the overlay's fields.
 */
export async function saveSettings($: EngineInterface, next: BackdropSettings): Promise<void> {
  const path = await settingsPath($)
  if (!path) throw new Error('Live Backdrop runs on Windows only.')
  const read = await readRaw($, path)
  if (read.kind === 'unreadable') throw new Error('the settings file is busy; try again')
  const merged = mergeSettings(read.kind === 'value' ? read.value : {}, next)
  await $.fs.write(path, `${JSON.stringify(merged, null, 2)}\n`)
}

/** Reads the colours the overlay took from the wallpaper, or null when there are none yet. */
export async function loadWallpaperColors($: EngineInterface): Promise<WallpaperColors | null> {
  const folder = await dataFolder($)
  return folder ? parseWallpaperColors(await readJson($, `${folder}\\wallpaper-colors.json`)) : null
}

/** The plugin's version, from its manifest; null when it cannot be read. */
export async function pluginVersion($: EngineInterface): Promise<string | null> {
  const manifest = await readJson($, `${$.plugin.root}/.claude-plugin/plugin.json`)
  const version = typeof manifest === 'object' && manifest !== null ? (manifest as Record<string, unknown>).version : null
  return typeof version === 'string' && /^\d+\.\d+\.\d+$/.test(version) ? version : null
}
