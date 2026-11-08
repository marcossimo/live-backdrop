import type { EngineInterface } from 'claude-code'

import type { BackdropSettings, OverlayStatus } from '../types'

const IMAGE_NAME = 'LiveBackdrop.exe'

// Windows answers both calls in well under a second; this only stops a stuck call.
const TIMEOUT_MS = 5000

/** Whether the overlay process is running, via `tasklist` (Windows only). */
export async function overlayStatus($: EngineInterface, settings: BackdropSettings): Promise<OverlayStatus> {
  if (!(await $.env.get('LOCALAPPDATA'))) return 'unsupported'
  try {
    const { stdout } = await $.process.run(['tasklist', '/FI', `IMAGENAME eq ${IMAGE_NAME}`, '/NH'], { timeoutMs: TIMEOUT_MS })
    if (String(stdout).toLowerCase().includes(IMAGE_NAME.toLowerCase())) return 'running'
    return settings.overlayPath ? 'stopped' : 'not-installed'
  } catch {
    return 'unknown'
  }
}

/**
 * Starts the overlay from the path it recorded on its last run. `start` detaches it, so it
 * outlives this session. Resolves false when there is nothing to start.
 */
export async function startOverlay($: EngineInterface, settings: BackdropSettings): Promise<boolean> {
  if (!settings.overlayPath) return false
  const { exitCode } = await $.process.run(['cmd', '/d', '/c', 'start', '""', settings.overlayPath], { timeoutMs: TIMEOUT_MS })
  return exitCode === 0
}
