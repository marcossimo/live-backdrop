import { atom, read, update } from 'claude-code'
import type { EngineInterface } from 'claude-code'

import type { BackdropSettings, ColorSetting, OverlayStatus, WallpaperColors } from '../types'
import { overlayStatus, startOverlay } from './overlay'
import { loadSettings, loadWallpaperColors, pluginVersion, saveSettings } from './settings'

/** The settings as last read or written; null until the first read. */
export const settingsAtom = atom({ plugin: 'live-backdrop', key: 'settings' } as const, null)

export const statusAtom = atom({ plugin: 'live-backdrop', key: 'status' } as const, 'unknown')

export const noticeAtom = atom({ plugin: 'live-backdrop', key: 'notice' } as const, '')

export const editingColorAtom = atom({ plugin: 'live-backdrop', key: 'editingColor' } as const, null)

export const wallpaperColorsAtom = atom({ plugin: 'live-backdrop', key: 'wallpaperColors' } as const, null)

export const versionAtom = atom({ plugin: 'live-backdrop', key: 'version' } as const, null)

export type PanelState = {
  settings: BackdropSettings | null
  status: OverlayStatus
  notice: string
  editingColor: ColorSetting | null
  wallpaperColors: WallpaperColors | null
  version: string | null
}

/**
 * What the panel draws from. Called while drawing, it subscribes the panel, so every later
 * update redraws it.
 */
export async function readPanelState($: EngineInterface): Promise<PanelState> {
  return {
    settings: await read($, settingsAtom),
    status: await read($, statusAtom),
    notice: await read($, noticeAtom),
    editingColor: await read($, editingColorAtom),
    wallpaperColors: await read($, wallpaperColorsAtom),
    version: await read($, versionAtom),
  }
}

/** Reads this plugin's version into the panel's state. */
export async function loadVersion($: EngineInterface): Promise<void> {
  const version = await pluginVersion($)
  await update($, versionAtom, () => version)
}

/** Shows (or, with null, hides) the custom-colour field of one colour setting. */
export async function editColor($: EngineInterface, setting: ColorSetting | null): Promise<void> {
  await update($, editingColorAtom, () => setting)
}

/** Shows a short line in the panel; empty clears it. */
export async function setNotice($: EngineInterface, notice: string): Promise<void> {
  await update($, noticeAtom, () => notice)
}

/** The settings as last read, or null before the first read. Subscribes a drawing. */
export async function readSettings($: EngineInterface): Promise<BackdropSettings | null> {
  return read($, settingsAtom)
}

/** The wallpaper's colours as last read, or null. Subscribes a drawing. */
export async function readWallpaperColors($: EngineInterface): Promise<WallpaperColors | null> {
  return read($, wallpaperColorsAtom)
}

// Every write redraws whatever reads the value (every message, for the colours), so a value is
// written only when it changed.
function same(a: unknown, b: unknown): boolean {
  return JSON.stringify(a) === JSON.stringify(b)
}

/**
 * Reads the settings file and the wallpaper's colours again. The overlay changes the settings
 * too (its tray icon and hotkey) and writes the colours as the wallpaper changes, so this runs
 * every few seconds.
 */
export async function sync($: EngineInterface): Promise<BackdropSettings> {
  const settings = await loadSettings($)
  const colors = settings.colorMode === 'wallpaper' ? await loadWallpaperColors($) : null
  if (!same(await read($, settingsAtom), settings)) await update($, settingsAtom, () => settings)
  if (!same(await read($, wallpaperColorsAtom), colors)) await update($, wallpaperColorsAtom, () => colors)
  return settings
}

/** Reads the settings, the wallpaper's colours and the overlay's state into the panel's state. */
export async function refresh($: EngineInterface): Promise<BackdropSettings> {
  const settings = await sync($)
  const status = await overlayStatus($, settings)
  await update($, statusAtom, () => status)
  return settings
}

/**
 * Applies a change to the settings as the file holds them now (the overlay may have changed
 * them since the panel last read them), saves it (the overlay applies it within a frame) and,
 * when the backdrop is on but the overlay is not running, starts it.
 */
export async function apply($: EngineInterface, patch: Partial<BackdropSettings>): Promise<void> {
  try {
    const next = { ...(await loadSettings($)), ...patch }
    await saveSettings($, next)
    await update($, settingsAtom, () => next)
    await setNotice($, '')
    if (next.enabled && (await overlayStatus($, next)) === 'stopped') {
      await setNotice($, 'Starting the overlay…')
      const started = await startOverlay($, next)
      await setNotice($, started ? '' : 'Could not start the overlay.')
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    await setNotice($, `Could not save: ${message}`)
  }
  await refresh($)
}
