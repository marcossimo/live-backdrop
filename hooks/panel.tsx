import type { EngineInterface, RenderInput } from 'claude-code'

import type { BackdropSettings, BlurMode, ColorSetting, OverlayStatus } from '../types'
import { clampBlurAmount, clampOpacity, parseHexColor } from './settings'
import { apply, editColor, readPanelState, refresh } from './state'

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
      return settings.enabled ? `On · ${percent(settings.opacity)}` : 'Off'
    case 'stopped':
      return settings.enabled ? 'Overlay not running' : 'Off'
    case 'not-installed':
      return 'Overlay not installed yet: run LiveBackdrop.exe once (see the README)'
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

  const { settings, status, notice, editingColor } = await readPanelState($)

  if (settings === null) {
    void refresh($).catch(() => undefined)
    return <Text dimColor>Reading settings…</Text>
  }

  // A press returns at once: saving (and starting the overlay) runs on, and the panel redraws
  // when it is done. Waiting here would hold the press past the app's time limit.
  const save = (patch: Partial<BackdropSettings>): void => {
    void apply($, { ...settings, ...patch }).catch(() => undefined)
  }
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

      <Button
        key="toggle"
        variant="primary"
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
        {colorRow('userBubbleColor', 'Your messages: bubble', "App's default")}
        {colorRow('userTextColor', 'Your messages: text', "App's default")}
        {colorRow('replyBackgroundColor', "Claude's replies: background", 'None')}
        {settings.replyBackgroundColor && shadeRow('replyBackgroundOpacity')}
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
        <Text bold>Startup</Text>
        <Button
          key="autostart"
          label={settings.startWithWindows ? 'Start with Windows: on' : 'Start with Windows: off'}
          onPress={() => save({ startWithWindows: !settings.startWithWindows })}
        />
      </Box>

      <Button key="refresh" dimColor label="Refresh" onPress={() => void refresh($).catch(() => undefined)} />
    </Box>
  )
}
