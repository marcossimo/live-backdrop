import type { EngineInterface } from 'claude-code'

import type { BackdropSettings } from '../types'
import { dataFolder, pluginVersion, readJson } from './settings'
import { refresh, setNotice } from './state'

const REPOSITORY = 'marcossimo/live-backdrop'
const ASSET = 'LiveBackdrop-win-x64.zip'
const VERSION = /^\d+\.\d+\.\d+$/

// An install status older than this is from an earlier install and no longer shown.
const STATUS_FRESH_MS = 10 * 60_000

/** Where an install puts the overlay, and the release it takes it from. */
export type InstallPlan = {
  version: string
  zipUrl: string
  checksumUrl: string
  installFolder: string
  statusPath: string
}

/** How far an install got, as its script reports it in `install-status.json`. */
export type InstallStatus = {
  state: 'downloading' | 'verifying' | 'installing' | 'done' | 'failed'
  message: string
  at: number
}

/** A PowerShell single-quoted literal: nothing inside it is expanded. */
export function psQuote(text: string): string {
  return `'${text.replace(/'/g, "''")}'`
}

/** The folder a path names a file in. */
function folderOf(path: string): string {
  const index = path.lastIndexOf('\\')
  return index > 0 ? path.slice(0, index) : path
}

/**
 * The install of overlay `version`, the plugin's own, so the two halves always match: from
 * that version's GitHub release, into the folder the overlay already runs from, or
 * `%LOCALAPPDATA%\Programs\LiveBackdrop` the first time.
 */
export function planInstall(version: string, localAppData: string, overlayPath: string | null): InstallPlan {
  if (!VERSION.test(version)) throw new Error(`unexpected plugin version "${version}"`)
  const base = `https://github.com/${REPOSITORY}/releases/download/v${version}/${ASSET}`
  return {
    version,
    zipUrl: base,
    checksumUrl: `${base}.sha256`,
    installFolder: overlayPath ? folderOf(overlayPath) : `${localAppData}\\Programs\\LiveBackdrop`,
    statusPath: `${localAppData}\\LiveBackdrop\\install-status.json`,
  }
}

/**
 * The PowerShell script that installs (or updates) the overlay: download the release and its
 * checksum, check the checksum, stop the running overlay, unpack over the old files with
 * `tar` (which, unlike Expand-Archive, does not mark the files as downloaded, so SmartScreen
 * does not stop the overlay later), start it, and report each step.
 */
export function installScript(plan: InstallPlan): string {
  const q = psQuote
  return `$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$status = ${q(plan.statusPath)}
function Report($state, $message) {
  $at = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
  [ordered]@{ state = $state; message = $message; version = ${q(plan.version)}; at = $at } | ConvertTo-Json | Set-Content -Encoding UTF8 -Path $status
}
$work = Join-Path $env:TEMP ('LiveBackdrop-' + [guid]::NewGuid().ToString('N'))
try {
  New-Item -ItemType Directory -Path $work | Out-Null
  Report 'downloading' ${q(`Downloading Live Backdrop ${plan.version}…`)}
  $zip = Join-Path $work ${q(ASSET)}
  $sum = Join-Path $work ${q(`${ASSET}.sha256`)}
  Invoke-WebRequest -UseBasicParsing -Uri ${q(plan.zipUrl)} -OutFile $zip
  Invoke-WebRequest -UseBasicParsing -Uri ${q(plan.checksumUrl)} -OutFile $sum
  Report 'verifying' 'Checking the download…'
  $expected = ((Get-Content -Raw -Path $sum).Trim() -split '\\s+')[0].ToLowerInvariant()
  $actual = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash.ToLowerInvariant()
  if ($expected -notmatch '^[0-9a-f]{64}$' -or $actual -ne $expected) { throw 'The download does not match its checksum.' }
  Report 'installing' 'Installing…'
  Get-Process -Name 'LiveBackdrop' -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 700
  $folder = ${q(plan.installFolder)}
  New-Item -ItemType Directory -Force -Path $folder | Out-Null
  & "$env:SystemRoot\\System32\\tar.exe" -xf $zip -C $folder
  if ($LASTEXITCODE -ne 0) { throw 'Could not unpack the download.' }
  Start-Process -FilePath (Join-Path $folder 'LiveBackdrop.exe')
  Report 'done' ${q(`Live Backdrop ${plan.version} is installed.`)}
} catch {
  Report 'failed' $_.Exception.Message
} finally {
  Remove-Item -Recurse -Force -Path $work -ErrorAction SilentlyContinue
}
`
}

/** Reads an install status, or null when there is none or it is old. */
export function parseInstallStatus(raw: unknown, now: number): InstallStatus | null {
  if (typeof raw !== 'object' || raw === null) return null
  const { state, message, at } = raw as Record<string, unknown>
  const states = ['downloading', 'verifying', 'installing', 'done', 'failed']
  if (typeof state !== 'string' || !states.includes(state) || typeof at !== 'number') return null
  if (now - at > STATUS_FRESH_MS) return null
  return { state: state as InstallStatus['state'], message: typeof message === 'string' ? message : '', at }
}

/**
 * Starts installing the overlay in the background: writes the script beside the settings and
 * starts PowerShell on it, hidden and detached, so it outlives this call. Progress arrives in
 * `install-status.json`, which {@link readInstallStatus} reads.
 */
export async function startInstall($: EngineInterface, settings: BackdropSettings): Promise<InstallPlan> {
  const localAppData = await $.env.get('LOCALAPPDATA')
  const folder = await dataFolder($)
  if (!localAppData || !folder) throw new Error('Live Backdrop runs on Windows only.')
  const version = await pluginVersion($)
  if (!version) throw new Error("could not read the plugin's version")

  const plan = planInstall(version, localAppData, settings.overlayPath)
  const scriptPath = `${folder}\\install.ps1`
  // A UTF-8 byte order mark, so Windows PowerShell reads the script's "…" right.
  await $.fs.write(scriptPath, `﻿${installScript(plan)}`)
  const { exitCode } = await $.process.run(
    [
      'cmd', '/d', '/c', 'start', '""', '/min',
      'powershell', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden',
      '-File', scriptPath,
    ],
    { timeoutMs: 10_000 },
  )
  if (exitCode !== 0) throw new Error('could not start PowerShell')
  return plan
}

/** The latest install's progress, or null when no install ran lately. */
export async function readInstallStatus($: EngineInterface): Promise<InstallStatus | null> {
  const folder = await dataFolder($)
  if (!folder) return null
  return parseInstallStatus(await readJson($, `${folder}\\install-status.json`), await $.clock.now())
}

/**
 * Starts installing (or updating) the overlay, and follows its progress in the panel's notice
 * until it is done. Resolves with a line for the person.
 */
export async function install($: EngineInterface): Promise<string> {
  const plan = await startInstall($, await refresh($))
  await setNotice($, `Installing Live Backdrop ${plan.version}…`)
  let seen = ''
  const follow = $.clock.every(1000, () => {
    void (async () => {
      const status = await readInstallStatus($)
      if (!status || status.message === seen) return
      seen = status.message
      await setNotice($, status.state === 'done' ? '' : status.message)
      if (status.state === 'done' || status.state === 'failed') {
        follow.cancel()
        await refresh($)
        await $.ui.toast(status.state === 'done' ? status.message : `Live Backdrop: ${status.message}`)
      }
    })().catch(() => undefined)
  })
  // Gives up following after a few minutes; the install itself goes on.
  $.clock.after(5 * 60_000, () => follow.cancel())
  return `Installing Live Backdrop ${plan.version} from GitHub (${plan.installFolder}). Progress shows in /backdrop.`
}
