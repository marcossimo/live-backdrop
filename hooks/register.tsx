import type { Register } from 'claude-code'

import { registerActivity } from './activity'
import { install } from './install'
import { registerMessageColors } from './messages'
import { PANEL_TITLE, renderPanel } from './panel'
import { apply, loadVersion, refresh, sync } from './state'

const PANEL_ID = 'live-backdrop-settings'

// The overlay changes the settings too (tray, hotkey) and writes the wallpaper's colours as it
// changes; they are read again this often.
const SYNC_MS = 3000

const USAGE = 'Usage: /backdrop, /backdrop on, /backdrop off or /backdrop install.'

/**
 * Live Backdrop for the Claude desktop app.
 *
 * `/backdrop` opens the settings panel; `/backdrop on` and `/backdrop off` switch the backdrop
 * without it; `/backdrop install` installs or updates the overlay. The picture itself is drawn
 * by a separate Windows program (the overlay, see `overlay/`), which follows the settings file
 * this plugin writes. Message colours are drawn by the app itself, through this plugin's render
 * hooks (see `messages.tsx`), and Claude's activity is reported to the overlay (`activity.ts`).
 */
export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    await $.command.register({
      name: 'backdrop',
      description: 'Live Backdrop settings (or: /backdrop on, /backdrop off, /backdrop install)',
    })
    // Clears a status line an earlier version may have left, and reads the settings the
    // message colours draw from.
    $.ui.status(undefined)
    void refresh($).catch(() => undefined)
    void loadVersion($).catch(() => undefined)
    $.clock.every(SYNC_MS, () => void sync($).catch(() => undefined))
    return next(e)
  })

  registerMessageColors(on)
  registerActivity(on)

  on('command.run', { command: 'backdrop' }, async ($, e) => {
    try {
      const argument = e.args.trim().toLowerCase()

      if (argument === 'on' || argument === 'off') {
        await apply($, { enabled: argument === 'on' })
        return { text: `Live Backdrop turned ${argument}.` }
      }

      if (argument === 'install' || argument === 'update') {
        return { text: await install($) }
      }

      if (argument !== '') {
        return { text: USAGE }
      }

      void refresh($).catch(() => undefined)
      const opened = await $.ui.open({ id: PANEL_ID, title: PANEL_TITLE, focus: true })
      return {
        text: opened.isPlaced
          ? 'Live Backdrop settings opened.'
          : `The settings panel could not open: ${opened.reason}`,
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      return { text: `Live Backdrop: ${message}` }
    }
  })

  on('ui.render', { component: 'Pane', requestId: 'live-backdrop-settings' }, ($, e) => renderPanel($, e))
}
