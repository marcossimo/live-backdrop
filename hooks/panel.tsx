import type { EngineInterface, RenderInput } from 'claude-code'

import type { BackdropSettings, BlurMode, ColorSetting, OverlayStatus } from '../types'
import { install } from './install'
import { clampBlurAmount, clampFps, clampOpacity, parseHexColor } from './settings'
import { apply, editColor, readPanelState, refresh, setNotice } from './state'

export const PANEL_TITLE = 'Live Backdrop'

const OPACITY_STEP = 0.05
const OPACITY_PRESETS = [0.05, 0.1, 0.15, 0.18, 0.2, 0.25, 0.3, 0.35, 0.4, 0.5, 0.6, 0.75]

const BLUR_STEP = 2
const BLUR_PRESETS = [4, 8, 12, 16, 20, 28, 40]

const BLUR_OPTIONS: { value: BlurMode; label: string }[] = [
  { value: 'text', label: 'Behind text' },
  { value: 'everywhere', label: 'Everywhere' },
  { value: 'off', label: 'Off' },
]

const COLOR_OFF = 'off'
const COLOR_CUSTOM = 'custom'
const SHADE_STEP = 0.02
const SHADE_PRESETS = [0.04, 0.06, 0.08, 0.1, 0.15, 0.2, 0.3, 0.4, 0.6]

/** Named colours offered in every colour list, before "Custom…". */
export const COLOR_PRESETS: { value: string; label: string }[] = [
  { value: '#ffffff', label: 'White' },
  { value: '#000000', label: 'Black' },
  { value: '#334155', label: 'Slate' },
  { value: '#2563eb', label: 'Blue' },
  { value: '#4f46e5', label: 'Indigo' },
  { value: '#7c3aed', label: 'Purple' },
  { value: '#db2777', label: 'Pink' },
  { value: '#dc2626', label: 'Red' },
  { value: '#ea580c', label: 'Orange' },
  { value: '#d97706', label: 'Amber' },
  { value: '#16a34a', label: 'Green' },
  { value: '#0d9488', label: 'Teal' },
]

/** The options of one colour list: off, the presets, the current custom colour, custom. */
export function colorOptions(current: string | null, offLabel: string): { value: string; label: string }[] {
  const options = [{ value: COLOR_OFF, label: offLabel }, ...COLOR_PRESETS]
  if (current && !COLOR_PRESETS.some(preset => preset.value === current)) {
    options.push({ value: current, label: current })
  }
  options.push({ value: COLOR_CUSTOM, label: 'Custom…' })
  return options
}

const FPS_OPTIONS = [
  { value: '0', label: "Wallpaper's own (up to 30)" },
  { value: '30', label: '30 frames a second' },
  { value: '20', label: '20 frames a second' },
  { value: '15', label: '15 frames a second' },
  { value: '10', label: '10 frames a second' },
  { value: '5', label: '5 frames a second' },
]

const HOTKEY = /^(?:(?:ctrl|control|alt|shift|win|windows)\+)+(?:[a-z0-9]|f(?:[1-9]|1[0-9]|2[0-4])|space|home|end|insert|delete|pageup|pagedown|up|down|left|right)$/i

/** A hotkey the overlay can register (modifiers, then one key), or empty for none. */
export function isValidHotkey(text: string): boolean {
  const compact = text.replace(/\s+/g, '')
  return compact === '' || HOTKEY.test(compact)
}

/** `Ctrl + alt+b` as `Ctrl+Alt+B`. */
export function formatHotkey(text: string): string {
  return text
    .split('+')
    .map(part => part.trim())
    .filter(part => part !== '')
    .map(part => part.charAt(0).toUpperCase() + part.slice(1).toLowerCase())
    .join('+')
}

/** What the install button offers, if anything: a first install, or an update to this plugin's version. */
export function installOffer(status: OverlayStatus, settings: BackdropSettings, version: string | null): string | null {
  if (status === 'unsupported' || version === null) return null
  if (status === 'not-installed') return `Install the overlay (${version})`
  if (settings.overlayPath && settings.overlayVersion !== version) return `Update the overlay to ${version}`
  return null
}

const onOff = (label: string, on: boolean): string => `${label}: ${on ? 'on' : 'off'}`

const SOURCE_OPTIONS = [
  { value: 'wallpaper', label: 'Live desktop wallpaper' },
  { value: 'image', label: 'Picture file' },
]

export function percent(value: number): string {
  return `${Math.round(value * 100)}%`
}

export function describeStatus(status: OverlayStatus, settings: BackdropSettings): string {
  switch (status) {
    case 'running':
      return (settings.enabled ? `On · ${percent(settings.opacity)}` : 'Off')
        + (settings.overlayVersion ? ` · overlay ${settings.overlayVersion}` : '')
    case 'stopped':
      return settings.enabled ? 'Overlay not running' : 'Off'
    case 'not-installed':
      return 'Overlay not installed yet: install it below, or with /backdrop install'
    case 'unsupported':
      return 'Live Backdrop runs on Windows only'
    default:
      return 'Overlay state unknown'
  }
}

/** The settings panel, drawn for `ui.render` on the panel's pane. */
export async function renderPanel($: EngineInterface, e: RenderInput<'Pane'>) {
  const ui = $.ui.resolve(e)
  const { Box, Text, Button } = ui
  // Not every surface draws fields: the mobile app has no Select or Input yet.
  const Select = 'Select' in ui ? ui.Select : null
  const Input = 'Input' in ui ? ui.Input : null

  const { settings, status, notice, editingColor, wallpaperColors, version } = await readPanelState($)

  if (settings === null) {
    void refresh($).catch(() => undefined)
    return <Text dimColor>Reading settings…</Text>
  }

  // A press returns at once: saving (and starting the overlay) runs on, and the panel redraws
  // when it is done. Waiting here would hold the press past the app's time limit.
  const save = (patch: Partial<BackdropSettings>): void => {
    void apply($, patch).catch(() => undefined)
  }
  const offer = installOffer(status, settings, version)
  const wallpaperMode = settings.colorMode === 'wallpaper'
  const presets = [...new Set([...OPACITY_PRESETS, settings.opacity])]
    .sort((a, b) => a - b)
    .map(value => ({ value: String(value), label: percent(value) }))
  const blurPresets = [...new Set([...BLUR_PRESETS, settings.blurAmount])]
    .sort((a, b) => a - b)
    .map(value => ({ value: String(value), label: `${value} px` }))

  // One colour setting: a list of presets, and a field for a custom #rrggbb.
  const colorRow = (setting: ColorSetting, label: string, offLabel: string) => (
    <Box flexDirection="column">
      <Text>{label}</Text>
      {Select && (
        <Select
          key={setting}
          value={settings[setting] ?? COLOR_OFF}
          options={colorOptions(settings[setting], offLabel)}
          onSelect={(value: string) => {
            if (value === COLOR_CUSTOM) {
              void editColor($, setting)
              return
            }
            void editColor($, null)
            void save({ [setting]: value === COLOR_OFF ? null : parseHexColor(value) })
          }}
        />
      )}
      {(editingColor === setting || !Select) && Input && (
        <Input
          key={`${setting}-custom`}
          label="Colour"
          placeholder="#rrggbb"
          value={settings[setting] ?? ''}
          submitLabel="use"
          onSubmit={(value: string) => {
            const color = parseHexColor(value)
            if (color === null && value.trim() !== '') return
            void editColor($, null)
            void save({ [setting]: color })
          }}
        />
      )}
    </Box>
  )

  // One opacity for a colour: - and +, and a list.
  const shadeRow = (setting: 'replyBackgroundOpacity') => (
    <Box flexDirection="row" gap={1} alignItems="center">
      <Button
        key={`${setting}-down`}
        label="−"
        onPress={() => save({ [setting]: clampOpacity(settings[setting] - SHADE_STEP) })}
      />
      <Text>{percent(settings[setting])}</Text>
      <Button
        key={`${setting}-up`}
        label="+"
        onPress={() => save({ [setting]: clampOpacity(settings[setting] + SHADE_STEP) })}
      />
      {Select && (
        <Select
          key={setting}
          value={String(settings[setting])}
          options={[...new Set([...SHADE_PRESETS, settings[setting]])]
            .sort((a, b) => a - b)
            .map(value => ({ value: String(value), label: percent(value) }))}
          onSelect={(value: string) => save({ [setting]: clampOpacity(Number(value)) })}
        />
      )}
    </Box>
  )

  return (
    <Box flexDirection="column" gap={1}>
      <Box flexDirection="column">
        <Text dimColor>{describeStatus(status, settings)}</Text>
        {notice !== '' && <Text color="warning">{notice}</Text>}
      </Box>

      {offer && (
        <Button
          key="install"
          variant="primary"
          label={offer}
          onPress={() => {
            void install($).catch(async (error: unknown) => {
              const message = error instanceof Error ? error.message : String(error)
              await setNotice($, `Could not install: ${message}`)
            })
          }}
        />
      )}

      <Button
        key="toggle"
        variant={offer ? undefined : 'primary'}
        label={settings.enabled ? 'Turn off' : 'Turn on'}
        onPress={() => save({ enabled: !settings.enabled })}
      />

      <Box flexDirection="column">
        <Text bold>Source</Text>
        {Select && (
          <Select
            key="source"
            value={settings.source}
            options={SOURCE_OPTIONS}
            onSelect={(value: string) => save({ source: value === 'image' ? 'image' : 'wallpaper' })}
          />
        )}
        {settings.source === 'image' && Input && (
          <Input
            key="image-path"
            label="Picture"
            placeholder="C:\Pictures\background.jpg"
            value={settings.imagePath ?? ''}
            submitLabel="use"
            onSubmit={(value: string) => save({ imagePath: value.trim() === '' ? null : value.trim() })}
          />
        )}
      </Box>

      <Box flexDirection="column">
        <Text bold>Opacity</Text>
        <Box flexDirection="row" gap={1} alignItems="center">
          <Button
            key="opacity-down"
            label="−"
            onPress={() => save({ opacity: clampOpacity(settings.opacity - OPACITY_STEP) })}
          />
          <Text>{percent(settings.opacity)}</Text>
          <Button
            key="opacity-up"
            label="+"
            onPress={() => save({ opacity: clampOpacity(settings.opacity + OPACITY_STEP) })}
          />
        </Box>
        {Select && (
          <Select
            key="opacity"
            value={String(settings.opacity)}
            options={presets}
            onSelect={(value: string) => save({ opacity: clampOpacity(Number(value)) })}
          />
        )}
      </Box>

      <Box flexDirection="column">
        <Text bold>Blur</Text>
        <Text dimColor>Softens the picture where the app shows text, so it stays easy to read.</Text>
        {Select && (
          <Select
            key="blur"
            value={settings.blur}
            options={BLUR_OPTIONS}
            onSelect={(value: string) =>
              save({ blur: BLUR_OPTIONS.find(option => option.value === value)?.value ?? 'text' })
            }
          />
        )}
        {settings.blur !== 'off' && (
          <Box flexDirection="row" gap={1} alignItems="center">
            <Button
              key="blur-down"
              label="−"
              onPress={() => save({ blurAmount: clampBlurAmount(settings.blurAmount - BLUR_STEP) })}
            />
            <Text>{`${settings.blurAmount} px`}</Text>
            <Button
              key="blur-up"
              label="+"
              onPress={() => save({ blurAmount: clampBlurAmount(settings.blurAmount + BLUR_STEP) })}
            />
            {Select && (
              <Select
                key="blur-amount"
                value={String(settings.blurAmount)}
                options={blurPresets}
                onSelect={(value: string) => save({ blurAmount: clampBlurAmount(Number(value)) })}
              />
            )}
          </Box>
        )}
      </Box>

      <Box flexDirection="column" gap={1}>
        <Box flexDirection="column">
          <Text bold>Colors</Text>
          <Text dimColor>Your messages and Claude's replies are recoloured by the app itself.</Text>
        </Box>
        <Button
          key="color-mode"
          label={onOff('Match the wallpaper', wallpaperMode)}
          onPress={() => save({ colorMode: wallpaperMode ? 'manual' : 'wallpaper' })}
        />
        {wallpaperMode && (
          <Box flexDirection="column">
            <Text dimColor>
              {wallpaperColors
                ? `Your bubble and the replies' background take the wallpaper's colour (${wallpaperColors.accent}) and follow it as it changes.`
                : status === 'running'
                  ? 'Waiting for the overlay to sample the wallpaper…'
                  : 'The overlay samples the wallpaper while it runs.'}
            </Text>
            <Text>Claude's replies: background</Text>
            {shadeRow('replyBackgroundOpacity')}
          </Box>
        )}
        {!wallpaperMode && colorRow('userBubbleColor', 'Your messages: bubble', "App's default")}
        {!wallpaperMode && colorRow('userTextColor', 'Your messages: text', "App's default")}
        {!wallpaperMode && colorRow('replyBackgroundColor', "Claude's replies: background", 'None')}
        {!wallpaperMode && settings.replyBackgroundColor && shadeRow('replyBackgroundOpacity')}
      </Box>

      <Box flexDirection="column">
        <Text bold>Claude at work</Text>
        <Text dimColor>The backdrop breathes, gently, while Claude is working, and settles when it is done.</Text>
        <Button
          key="react"
          label={onOff('Breathe while Claude works', settings.reactToClaude)}
          onPress={() => save({ reactToClaude: !settings.reactToClaude })}
        />
      </Box>

      <Box flexDirection="column">
        <Text bold>Power</Text>
        <Text dimColor>Fewer frames, or a still picture, use less of the computer.</Text>
        {Select && (
          <Select
            key="max-fps"
            value={String(settings.maxFps)}
            options={FPS_OPTIONS.some(option => option.value === String(settings.maxFps))
              ? FPS_OPTIONS
              : [...FPS_OPTIONS, { value: String(settings.maxFps), label: `${settings.maxFps} frames a second` }]}
            onSelect={(value: string) => save({ maxFps: clampFps(Number(value)) })}
          />
        )}
        <Button
          key="pause-battery"
          label={onOff('Hold still on battery', settings.pauseOnBattery)}
          onPress={() => save({ pauseOnBattery: !settings.pauseOnBattery })}
        />
        <Button
          key="pause-background"
          label={onOff('Hold still while Claude is in the background', settings.pauseInBackground)}
          onPress={() => save({ pauseInBackground: !settings.pauseInBackground })}
        />
      </Box>

      <Box flexDirection="column">
        <Text bold>Web pages</Text>
        <Text dimColor>The built-in browser and other embedded pages, shown without a backdrop.</Text>
        <Button
          key="clear-web-pages"
          label={settings.clearWebPages ? 'Keep web pages clear: on' : 'Keep web pages clear: off'}
          onPress={() => save({ clearWebPages: !settings.clearWebPages })}
        />
      </Box>

      <Box flexDirection="column">
        <Text bold>Startup and shortcuts</Text>
        <Button
          key="autostart"
          label={onOff('Start with Windows', settings.startWithWindows)}
          onPress={() => save({ startWithWindows: !settings.startWithWindows })}
        />
        <Button
          key="tray"
          label={onOff('Icon in the notification area', settings.trayIcon)}
          onPress={() => save({ trayIcon: !settings.trayIcon })}
        />
        {Input && (
          <Input
            key="hotkey"
            label="On/off shortcut"
            placeholder="Ctrl+Alt+B (empty: none)"
            value={settings.hotkey}
            submitLabel="use"
            onSubmit={(value: string) => {
              if (!isValidHotkey(value)) {
                void setNotice($, 'A shortcut is one or more of Ctrl, Alt, Shift, Win and one key, like Ctrl+Alt+B.')
                return
              }
              save({ hotkey: formatHotkey(value) })
            }}
          />
        )}
      </Box>

      <Button key="refresh" dimColor label="Refresh" onPress={() => void refresh($).catch(() => undefined)} />
    </Box>
  )
}
