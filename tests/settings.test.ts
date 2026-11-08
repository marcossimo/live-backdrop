import { expect, test } from 'claude-code/testing'

import { DEFAULTS, clampBlurAmount, clampOpacity, mergeSettings, parseSettings } from '../hooks/settings'

test('an empty or unreadable file yields the defaults', async () => {
  expect(parseSettings({})).toEqual(DEFAULTS)
  expect(parseSettings(null)).toEqual(DEFAULTS)
  expect(parseSettings([1, 2])).toEqual(DEFAULTS)
  expect(parseSettings('text')).toEqual(DEFAULTS)
})

test('every field is read', async () => {
  const settings = parseSettings({
    version: 1,
    enabled: false,
    source: 'image',
    imagePath: 'C:\\Pictures\\forest.jpg',
    opacity: 0.4,
    startWithWindows: true,
    blur: 'everywhere',
    blurAmount: 20,
    clearWebPages: false,
    overlayPath: 'C:\\Tools\\LiveBackdrop.exe',
  })

  expect(settings).toEqual({
    ...DEFAULTS,
    enabled: false,
    source: 'image',
    imagePath: 'C:\\Pictures\\forest.jpg',
    opacity: 0.4,
    startWithWindows: true,
    blur: 'everywhere',
    blurAmount: 20,
    clearWebPages: false,
    overlayPath: 'C:\\Tools\\LiveBackdrop.exe',
  })
})

test('wrong types fall back to the defaults', async () => {
  expect(
    parseSettings({ enabled: 'yes', opacity: 'high', source: 3, imagePath: '  ', blur: 'max', blurAmount: '9', clearWebPages: 1 }),
  ).toEqual(DEFAULTS)
})

test('opacity is clamped and rounded to whole percents', async () => {
  expect(clampOpacity(-1)).toBe(0)
  expect(clampOpacity(2.5)).toBe(1)
  expect(clampOpacity(0.123)).toBe(0.12)
  expect(clampOpacity(Number.NaN)).toBe(DEFAULTS.opacity)
})

test('saving keeps fields the plugin does not own', async () => {
  const merged = mergeSettings(
    { overlayPath: 'C:\\Tools\\LiveBackdrop.exe', future: { a: 1 }, opacity: 0.9 },
    { ...DEFAULTS, opacity: 0.3 },
  )

  expect(merged.overlayPath).toBe('C:\\Tools\\LiveBackdrop.exe')
  expect(merged.future).toEqual({ a: 1 })
  expect(merged.opacity).toBe(0.3)
  expect(merged.version).toBe(1)
})

test('blur strength is clamped to whole pixels', async () => {
  expect(clampBlurAmount(-3)).toBe(0)
  expect(clampBlurAmount(100)).toBe(40)
  expect(clampBlurAmount(7.6)).toBe(8)
  expect(clampBlurAmount(Number.POSITIVE_INFINITY)).toBe(DEFAULTS.blurAmount)
})
