import { expect, test } from 'claude-code/testing'

import { DEFAULTS } from '../hooks/settings'

const PANE = {
  plugin: 'live-backdrop',
  component: 'Pane',
  requestId: 'live-backdrop-settings',
  props: {
    title: 'Live Backdrop',
    isFocused: true,
    bodyColumns: 60,
    placement: 'dock',
    scroll: { offset: 0, bodyRows: 40 },
    view: {},
  },
} as const

// The panel's state as the plugin would hold it, by key; anything else goes on to the engine.
type Values = Record<string, unknown>

test('the panel draws every section, with the install offer when the overlay is missing', async ($, on) => {
  const values: Values = {
    settings: DEFAULTS,
    status: 'not-installed',
    notice: '',
    editingColor: null,
    wallpaperColors: null,
    version: '0.4.0',
  }
  on('state.get', (_, e, next) =>
    e.key in values ? ({ value: { value: values[e.key], version: 1 } } as never) : next(e),
  )

  for (const surface of ['desktop', 'terminal'] as const) {
    const ui = await $.ui.mount({ ...PANE, surface })
    expect(await ui.find({ type: 'Button', key: 'install' })).toBeTruthy()
    expect(await ui.find({ type: 'Button', key: 'color-mode' })).toBeTruthy()
    expect(await ui.find({ type: 'Button', key: 'react' })).toBeTruthy()
    expect(await ui.find({ type: 'Button', key: 'pause-battery' })).toBeTruthy()
    expect(await ui.find({ type: 'Button', key: 'tray' })).toBeTruthy()
    expect(await ui.find({ type: 'Text', text: /Claude at work/ })).toBeTruthy()
    await ui.unmount()
  }
})

test('in wallpaper mode the panel shows the sampled colour instead of the colour lists', async ($, on) => {
  const values: Values = {
    settings: { ...DEFAULTS, colorMode: 'wallpaper', overlayPath: 'C:\\x\\LiveBackdrop.exe', overlayVersion: '0.4.0' },
    status: 'running',
    notice: '',
    editingColor: null,
    wallpaperColors: { accent: '#7c3aed', text: '#ffffff', average: '#222222' },
    version: '0.4.0',
  }
  on('state.get', (_, e, next) =>
    e.key in values ? ({ value: { value: values[e.key], version: 1 } } as never) : next(e),
  )

  const ui = await $.ui.mount({ ...PANE, surface: 'desktop' })
  expect(await ui.find({ type: 'Text', text: /#7c3aed/ })).toBeTruthy()
  expect(await ui.find({ type: 'Button', key: 'install' })).toBeFalsy()
  await ui.unmount()
})
