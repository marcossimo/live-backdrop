import { atom, read, update } from 'claude-code'
import type { EngineInterface } from 'claude-code'

import type { BackdropSettings, ColorSetting, OverlayStatus } from '../types'
import { overlayStatus, startOverlay } from './overlay'
import { loadSettings, saveSettings } from './settings'

/** The settings as last read or written; null until the first read. */
export const settingsAtom = atom({ plugin: 'live-backdrop', key: 'settings' } as const, null)

export const statusAtom = atom({ plugin: 'live-backdrop', key: 'status' } as const, 'unknown')

export const noticeAtom = atom({ plugin: 'live-backdrop', key: 'notice' } as const, '')

export const editingColorAtom = atom({ plugin: 'live-backdrop', key: 'editingColor' } as const, null)

export type PanelState = {
  settings: BackdropSettings | null
  status: OverlayStatus
  notice: string
  editingColor: ColorSetting | null
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
  }
}

/** Shows (or, with null, hides) the custom-colour field of one colour setting. */
export async function editColor($: EngineInterface, setting: ColorSetting | null): Promise<void> {
  await update($, editingColorAtom, () => setting)
}

/** The settings as last read, or null before the first read. Subscribes a drawing. */
export async function readSettings($: EngineInterface): Promise<BackdropSettings | null> {
  return read($, settingsAtom)
}

/** Reads the settings file and the overlay's state into the panel's state. */
export async function refresh($: EngineInterface): Promise<BackdropSettings> {
  const settings = await loadSettings($)
  await update($, settingsAtom, () => settings)
  const status = await overlayStatus($, settings)
  await update($, statusAtom, () => status)
  return settings
}

/**
 * Saves new settings (the overlay applies them within a frame) and, when the backdrop is on but
 * the overlay is not running, starts it.
 */
export async function apply($: EngineInterface, next: BackdropSettings): Promise<void> {
  try {
    await saveSettings($, next)
    await update($, settingsAtom, () => next)
    await update($, noticeAtom, () => '')
    if (next.enabled && (await overlayStatus($, next)) === 'stopped') {
      await update($, noticeAtom, () => 'Starting the overlay…')
      const started = await startOverlay($, next)
      await update($, noticeAtom, () => (started ? '' : 'Could not start the overlay.'))
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    await update($, noticeAtom, () => `Could not save: ${message}`)
  }
  await refresh($)
}
