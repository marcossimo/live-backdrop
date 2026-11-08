import { expect, test } from 'claude-code/testing'

import { COLOR_PRESETS, colorOptions } from '../hooks/panel'
import { DEFAULTS, mergeSettings, parseHexColor, parseSettings, withOpacity } from '../hooks/settings'

test('hex colours are accepted, lowercased, and anything else is refused', async () => {
  expect(parseHexColor('#1D4ED8')).toBe('#1d4ed8')
  expect(parseHexColor(' #fff ')).toBe('#fff')
  expect(parseHexColor('white')).toBe(null)
  expect(parseHexColor('#12345')).toBe(null)
  expect(parseHexColor('rgba(0,0,0,1)')).toBe(null)
  expect(parseHexColor(42)).toBe(null)
})

test('a colour at an opacity becomes rgba()', async () => {
  expect(withOpacity('#ffffff', 0.08)).toBe('rgba(255, 255, 255, 0.08)')
  expect(withOpacity('#f0a', 0.5)).toBe('rgba(255, 0, 170, 0.5)')
  expect(withOpacity('#000000', 7)).toBe('rgba(0, 0, 0, 1)')
})

test('colour settings are off by default and read when valid', async () => {
  expect(parseSettings({}).userBubbleColor).toBe(null)
  expect(parseSettings({}).replyBackgroundOpacity).toBe(DEFAULTS.replyBackgroundOpacity)

  const settings = parseSettings({
    userBubbleColor: '#2563EB',
    userTextColor: 'not a colour',
    replyBackgroundColor: '#ffffff',
    replyBackgroundOpacity: 2,
  })

  expect(settings.userBubbleColor).toBe('#2563eb')
  expect(settings.userTextColor).toBe(null)
  expect(settings.replyBackgroundColor).toBe('#ffffff')
  expect(settings.replyBackgroundOpacity).toBe(1)
})

test('colour settings are saved', async () => {
  const merged = mergeSettings({}, { ...DEFAULTS, userBubbleColor: '#2563EB', replyBackgroundOpacity: 0.1 })

  expect(merged.userBubbleColor).toBe('#2563eb')
  expect(merged.replyBackgroundOpacity).toBe(0.1)
  expect(merged.userTextColor).toBe(null)
})

test('the colour list offers off, the presets, a custom colour in use, and custom', async () => {
  const preset = colorOptions('#2563eb', 'None')
  expect(preset[0]).toEqual({ value: 'off', label: 'None' })
  expect(preset.length).toBe(COLOR_PRESETS.length + 2)

  const custom = colorOptions('#123456', 'None')
  expect(custom.some(option => option.value === '#123456')).toBe(true)
  expect(custom[custom.length - 1]?.value).toBe('custom')
})
