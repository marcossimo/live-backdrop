import { expect, test } from 'claude-code/testing'

import { activityFileName, activityJson } from '../hooks/activity'
import { installScript, parseInstallStatus, planInstall, psQuote } from '../hooks/install'
import { formatHotkey, installOffer, isValidHotkey } from '../hooks/panel'
import {
  DEFAULTS,
  clampFps,
  mergeSettings,
  messageColors,
  parseSettings,
  parseWallpaperColors,
} from '../hooks/settings'

test('the new settings have defaults and are read when valid', async () => {
  const defaults = parseSettings({})
  expect(defaults.colorMode).toBe('manual')
  expect(defaults.reactToClaude).toBe(true)
  expect(defaults.maxFps).toBe(0)
  expect(defaults.pauseOnBattery).toBe(true)
  expect(defaults.pauseInBackground).toBe(false)
  expect(defaults.trayIcon).toBe(true)
  expect(defaults.hotkey).toBe('Ctrl+Alt+B')
  expect(defaults.overlayVersion).toBe(null)

  const settings = parseSettings({
    colorMode: 'wallpaper',
    reactToClaude: false,
    maxFps: 15,
    pauseOnBattery: false,
    pauseInBackground: true,
    trayIcon: false,
    hotkey: ' Win+F9 ',
    overlayVersion: '0.4.0',
  })
  expect(settings).toEqual({
    ...DEFAULTS,
    colorMode: 'wallpaper',
    reactToClaude: false,
    maxFps: 15,
    pauseOnBattery: false,
    pauseInBackground: true,
    trayIcon: false,
    hotkey: 'Win+F9',
    overlayVersion: '0.4.0',
  })
})

test('wrong types and unknown modes fall back to the defaults', async () => {
  expect(
    parseSettings({ colorMode: 'rainbow', reactToClaude: 'yes', maxFps: '30', trayIcon: 0, hotkey: 5 }),
  ).toEqual(DEFAULTS)
})

test('frame rates are whole and within range', async () => {
  expect(clampFps(-1)).toBe(0)
  expect(clampFps(500)).toBe(60)
  expect(clampFps(14.6)).toBe(15)
  expect(clampFps(Number.NaN)).toBe(0)
})

test('saving writes the new fields and keeps the overlay its own', async () => {
  const merged = mergeSettings(
    { overlayPath: 'C:\\Tools\\LiveBackdrop.exe', overlayVersion: '0.4.0', enabled: false },
    { ...DEFAULTS, colorMode: 'wallpaper', maxFps: 10, hotkey: ' Ctrl+Alt+B ' },
  )
  expect(merged.overlayPath).toBe('C:\\Tools\\LiveBackdrop.exe')
  expect(merged.overlayVersion).toBe('0.4.0')
  expect(merged.colorMode).toBe('wallpaper')
  expect(merged.maxFps).toBe(10)
  expect(merged.hotkey).toBe('Ctrl+Alt+B')
})

test("the wallpaper's colours are read only when all are valid", async () => {
  expect(parseWallpaperColors({ accent: '#7C3AED', text: '#ffffff', average: '#222222' })).toEqual({
    accent: '#7c3aed',
    text: '#ffffff',
    average: '#222222',
  })
  expect(parseWallpaperColors({ accent: '#7c3aed', text: 'white', average: '#222222' })).toBe(null)
  expect(parseWallpaperColors(null)).toBe(null)
})

test('messages take the wallpaper colours in wallpaper mode, once there are some', async () => {
  const wallpaper = { accent: '#7c3aed', text: '#ffffff', average: '#222222' }
  const manual = { ...DEFAULTS, userBubbleColor: '#2563eb', userTextColor: '#000000', replyBackgroundColor: null }

  expect(messageColors(manual, wallpaper)).toEqual({
    userBubbleColor: '#2563eb',
    userTextColor: '#000000',
    replyBackgroundColor: null,
  })
  expect(messageColors({ ...manual, colorMode: 'wallpaper' }, wallpaper)).toEqual({
    userBubbleColor: '#7c3aed',
    userTextColor: '#ffffff',
    replyBackgroundColor: '#7c3aed',
  })
  expect(messageColors({ ...manual, colorMode: 'wallpaper' }, null).userBubbleColor).toBe('#2563eb')
})

test('hotkeys need a modifier and one key', async () => {
  expect(isValidHotkey('Ctrl+Alt+B')).toBe(true)
  expect(isValidHotkey('win + shift + f9')).toBe(true)
  expect(isValidHotkey('')).toBe(true)
  expect(isValidHotkey('B')).toBe(false)
  expect(isValidHotkey('Ctrl+Alt')).toBe(false)
  expect(isValidHotkey('Ctrl+F25')).toBe(false)
  expect(formatHotkey('ctrl + alt+b')).toBe('Ctrl+Alt+B')
})

test('the install is offered when the overlay is missing or older than the plugin', async () => {
  const installed = { ...DEFAULTS, overlayPath: 'C:\\x\\LiveBackdrop.exe', overlayVersion: '0.4.0' }
  expect(installOffer('not-installed', DEFAULTS, '0.4.0')).toBe('Install the overlay (0.4.0)')
  expect(installOffer('running', installed, '0.4.0')).toBe(null)
  expect(installOffer('running', { ...installed, overlayVersion: null }, '0.4.0')).toBe('Update the overlay to 0.4.0')
  expect(installOffer('unsupported', DEFAULTS, '0.4.0')).toBe(null)
  expect(installOffer('not-installed', DEFAULTS, null)).toBe(null)
})

test('an install takes the plugin version from GitHub, into the folder the overlay runs from', async () => {
  const first = planInstall('0.4.0', 'C:\\Users\\me\\AppData\\Local', null)
  expect(first.zipUrl).toBe(
    'https://github.com/marcossimo/live-backdrop/releases/download/v0.4.0/LiveBackdrop-win-x64.zip',
  )
  expect(first.checksumUrl).toBe(`${first.zipUrl}.sha256`)
  expect(first.installFolder).toBe('C:\\Users\\me\\AppData\\Local\\Programs\\LiveBackdrop')
  expect(first.statusPath).toBe('C:\\Users\\me\\AppData\\Local\\LiveBackdrop\\install-status.json')

  const update = planInstall('0.4.0', 'C:\\Users\\me\\AppData\\Local', 'D:\\Tools\\Backdrop\\LiveBackdrop.exe')
  expect(update.installFolder).toBe('D:\\Tools\\Backdrop')

  expect(() => planInstall('latest', 'C:\\x', null)).toThrow()
})

test('the install script quotes every value and checks the download', async () => {
  expect(psQuote("C:\\Users\\O'Brien")).toBe("'C:\\Users\\O''Brien'")

  const script = installScript(planInstall('0.4.0', "C:\\Users\\O'Brien\\AppData\\Local", null))
  expect(script).toContain("'C:\\Users\\O''Brien\\AppData\\Local\\Programs\\LiveBackdrop'")
  expect(script).toContain('Get-FileHash -Algorithm SHA256')
  expect(script).toContain("throw 'The download does not match its checksum.'")
  expect(script).toContain('tar.exe" -xf $zip -C $folder')
  expect(script).toContain("-split '\\s+'")
})

test('an install status is shown while it is fresh', async () => {
  const now = 1_000_000_000
  expect(parseInstallStatus({ state: 'verifying', message: 'Checking…', at: now - 1000 }, now)).toEqual({
    state: 'verifying',
    message: 'Checking…',
    at: now - 1000,
  })
  expect(parseInstallStatus({ state: 'done', message: '', at: now - 3_600_000 }, now)).toBe(null)
  expect(parseInstallStatus({ state: 'exploded', at: now }, now)).toBe(null)
  expect(parseInstallStatus('nope', now)).toBe(null)
})

test("a session's activity file is named safely and says whether Claude works", async () => {
  expect(activityFileName('3f2a-11ee_b')).toBe('3f2a-11ee_b.json')
  expect(activityFileName('..\\..\\evil')).toBe('evil.json')
  expect(activityFileName('///')).toBe('session.json')
  expect(JSON.parse(activityJson(true, 0))).toEqual({ working: true, at: '1970-01-01T00:00:00.000Z' })
})
