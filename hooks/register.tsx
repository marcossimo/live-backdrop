import type { Register } from 'claude-code'

import { registerMessageColors } from './messages'
import { PANEL_TITLE, renderPanel } from './panel'
import { apply, refresh } from './state'

const PANEL_ID = 'live-backdrop-settings'

/**
 * Live Backdrop for the Claude desktop app.
 *
 * `/backdrop` opens the settings panel; `/backdrop on` and `/backdrop off` switch the backdrop
 * without it. The picture itself is drawn by a separate Windows program (the overlay, see
 * `overlay/`), which follows the settings file this plugin writes. Message colours are drawn
 * by the app itself, through this plugin's render hooks (see `messages.tsx`).
 */
export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    await $.command.register({
      name: 'backdrop',
      description: 'Live Backdrop settings (or: /backdrop on, /backdrop off)',
    })
    // Clears a status line an earlier version may have left, and reads the settings the
    // message colours draw from.
    $.ui.status(undefined)
    void refresh($).catch(() => undefined)
    return next(e)
  })

  registerMessageColors(on)

  on('command.run', { command: 'backdrop' }, async ($, e) => {
    try {
      const argument = e.args.trim().toLowerCase()

      if (argument === 'on' || argument === 'off') {
        const settings = await refresh($)
        await apply($, { ...settings, enabled: argument === 'on' })
        return { text: `Live Backdrop turned ${argument}.` }
      }

      if (argument !== '') {
        return { text: 'Usage: /backdrop, /backdrop on or /backdrop off.' }
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
