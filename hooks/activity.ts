import type { EngineInterface, On } from 'claude-code'

import { dataFolder } from './settings'
import { readSettings } from './state'

// A turn writes its file again this often while it runs, so the overlay can tell a working
// session from one that ended without saying so (it ignores files older than 15 minutes).
const HEARTBEAT_MS = 60_000

// This session's turn, as last reported. A reload starts them over, which only means one
// "idle" write may be skipped; the overlay's staleness rule covers it.
let working = false
let heartbeat: { cancel: () => void } | null = null

/** The file name a session id maps to: letters, digits, dashes and underscores only. */
export function activityFileName(sessionId: string): string {
  const safe = sessionId.replace(/[^A-Za-z0-9_-]/g, '')
  return `${safe === '' ? 'session' : safe}.json`
}

/** The content of a session's activity file. */
export function activityJson(state: boolean, now: number): string {
  return `${JSON.stringify({ working: state, at: new Date(now).toISOString() })}\n`
}

async function writeActivity($: EngineInterface, state: boolean, sessionId?: string): Promise<void> {
  const folder = await dataFolder($)
  if (!folder) return
  const id = sessionId ?? (await $.session.id())
  await $.fs.write(`${folder}\\activity\\${activityFileName(id)}`, activityJson(state, await $.clock.now()))
}

async function startWorking($: EngineInterface): Promise<void> {
  // Nothing to tell when the backdrop does not react.
  if ((await readSettings($))?.reactToClaude === false) return
  working = true
  await writeActivity($, true)
  heartbeat?.cancel()
  heartbeat = $.clock.every(HEARTBEAT_MS, () => void writeActivity($, true).catch(() => undefined))
}

async function stopWorking($: EngineInterface, sessionId?: string): Promise<void> {
  heartbeat?.cancel()
  heartbeat = null
  if (!working) return
  working = false
  await writeActivity($, false, sessionId)
}

/**
 * Tells the overlay when Claude is working, so the backdrop can breathe: one file per session
 * in `%LOCALAPPDATA%\LiveBackdrop\activity`, `working: true` from the start of a main-loop
 * turn until its end. Subagents' turns run inside the main one and are not reported apart.
 * Writing never holds up the turn, and a failed write only means no breathing.
 */
export function registerActivity(on: On): void {
  on('turn.start', ($, e, next) => {
    if (e.agentId === undefined) void startWorking($).catch(() => undefined)
    return next(e)
  })

  on('turn.complete', ($, e, next) => {
    if (e.agentId === undefined) void stopWorking($).catch(() => undefined)
    return next(e)
  })

  on('session.end', ($, e, next) => {
    void stopWorking($, e.sessionId).catch(() => undefined)
    return next(e)
  })
}
