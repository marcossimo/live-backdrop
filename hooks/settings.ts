import type { EngineInterface } from 'claude-code'

import type { BackdropSettings, BlurMode } from '../types'

/** The settings file format version this plugin writes. Keep in step with the overlay. */
export const SETTINGS_VERSION = 1

export const DEFAULT_OPACITY = 0.18

export const DEFAULT_BLUR_AMOUNT = 12

export const MAX_BLUR_AMOUNT = 40

const BLUR_MODES: readonly BlurMode[] = ['off', 'text', 'everywhere']

export const DEFAULT_REPLY_BACKGROUND_OPACITY = 0.08

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
  overlayPath: null,
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

  return {
    enabled: typeof root.enabled === 'boolean' ? root.enabled : DEFAULTS.enabled,
    source: root.source === 'image' ? 'image' : 'wallpaper',
    imagePath: text('imagePath'),
    opacity: typeof root.opacity === 'number' ? clampOpacity(root.opacity) : DEFAULTS.opacity,
    startWithWindows:
      typeof root.startWithWindows === 'boolean' ? root.startWithWindows : DEFAULTS.startWithWindows,
    blur: BLUR_MODES.find(mode => mode === root.blur) ?? DEFAULTS.blur,
    blurAmount: typeof root.blurAmount === 'number' ? clampBlurAmount(root.blurAmount) : DEFAULTS.blurAmount,
    clearWebPages: typeof root.clearWebPages === 'boolean' ? root.clearWebPages : DEFAULTS.clearWebPages,
    userBubbleColor: parseHexColor(root.userBubbleColor),
    userTextColor: parseHexColor(root.userTextColor),
    replyBackgroundColor: parseHexColor(root.replyBackgroundColor),
    replyBackgroundOpacity: fraction('replyBackgroundOpacity', DEFAULTS.replyBackgroundOpacity),
    overlayPath: text('overlayPath'),
  }
}

/**
 * The file's new content: the fields this plugin owns, laid over what was there, so fields
 * written by the overlay or a newer version survive.
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
  }
}

/** `%LOCALAPPDATA%\LiveBackdrop\settings.json`, or null off Windows. */
export async function settingsPath($: EngineInterface): Promise<string | null> {
  const localAppData = await $.env.get('LOCALAPPDATA')
  return localAppData ? `${localAppData}\\LiveBackdrop\\settings.json` : null
}

async function readRaw($: EngineInterface, path: string): Promise<unknown> {
  try {
    return JSON.parse(String(await $.fs.read(path)))
  } catch {
    // A missing or half-written file reads as empty: every field takes its default.
    return {}
  }
}

export async function loadSettings($: EngineInterface): Promise<BackdropSettings> {
  const path = await settingsPath($)
  return path ? parseSettings(await readRaw($, path)) : DEFAULTS
}

export async function saveSettings($: EngineInterface, next: BackdropSettings): Promise<void> {
  const path = await settingsPath($)
  if (!path) throw new Error('Live Backdrop runs on Windows only.')
  const merged = mergeSettings(await readRaw($, path), next)
  await $.fs.write(path, `${JSON.stringify(merged, null, 2)}\n`)
}
