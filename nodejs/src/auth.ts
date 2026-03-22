import { mkdir, readFile, rm, writeFile, chmod } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'

import { DEFAULT_BASE_URL, fetchQrCode, pollQrStatus } from './api.js'

const DEFAULT_TOKEN_DIR = path.join(os.homedir(), '.weixin-bot')
const DEFAULT_TOKEN_PATH = path.join(DEFAULT_TOKEN_DIR, 'credentials.json')
const QR_POLL_INTERVAL_MS = 2_000

export interface Credentials {
  token: string
  baseUrl: string
  accountId: string
  userId: string
}

export interface LoginOptions {
  baseUrl?: string
  tokenPath?: string
  force?: boolean
}

function resolveTokenPath(tokenPath?: string): string {
  return tokenPath ?? DEFAULT_TOKEN_PATH
}

function log(message: string): void {
  process.stderr.write(`[weixin-bot] ${message}\n`)
}

async function saveCredentials(credentials: Credentials, tokenPath?: string): Promise<void> {
  const targetPath = resolveTokenPath(tokenPath)
  await mkdir(path.dirname(targetPath), { recursive: true, mode: 0o700 })
  await writeFile(targetPath, `${JSON.stringify(credentials, null, 2)}\n`, { mode: 0o600 })
  await chmod(targetPath, 0o600)
}

function isCredentials(value: unknown): value is Credentials {
  if (!value || typeof value !== 'object') {
    return false
  }

  const candidate = value as Record<string, unknown>
  return typeof candidate.token === 'string'
    && typeof candidate.baseUrl === 'string'
    && typeof candidate.accountId === 'string'
    && typeof candidate.userId === 'string'
}

async function printQrInstructions(url: string): Promise<void> {
  // Try to render QR in terminal if qrcode-terminal is available
  try {
    // @ts-ignore — optional peer, no types needed
    const qrterm = await import('qrcode-terminal')
    ;(qrterm.default ?? qrterm).generate(url, { small: true }, (qr: string) => {
      process.stderr.write(`${qr}\n`)
    })
  } catch {
    // no-op: fall through to URL display
  }
  log('用微信扫描上方二维码，或在微信中打开以下链接:')
  process.stderr.write(`${url}\n`)
}

export async function loadCredentials(tokenPath?: string): Promise<Credentials | undefined> {
  const targetPath = resolveTokenPath(tokenPath)

  try {
    const raw = await readFile(targetPath, 'utf8')
    const parsed = JSON.parse(raw) as unknown

    if (!isCredentials(parsed)) {
      throw new Error(`Invalid credentials format in ${targetPath}`)
    }

    return parsed
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') {
      return undefined
    }

    throw error
  }
}

export async function clearCredentials(tokenPath?: string): Promise<void> {
  await rm(resolveTokenPath(tokenPath), { force: true })
}

export async function login(options: LoginOptions = {}): Promise<Credentials> {
  const baseUrl = options.baseUrl ?? DEFAULT_BASE_URL

  if (!options.force) {
    const existing = await loadCredentials(options.tokenPath)
    if (existing) {
      return existing
    }
  }

  for (;;) {
    const qr = await fetchQrCode(baseUrl)
    await printQrInstructions(qr.qrcode_img_content)

    let lastStatus: string | undefined

    for (;;) {
      const status = await pollQrStatus(baseUrl, qr.qrcode)

      if (status.status !== lastStatus) {
        if (status.status === 'scaned') {
          log('QR code scanned. Confirm the login inside WeChat.')
        } else if (status.status === 'confirmed') {
          log('Login confirmed.')
        } else if (status.status === 'expired') {
          log('QR code expired. Requesting a new one...')
        }
        lastStatus = status.status
      }

      if (status.status === 'confirmed') {
        if (!status.bot_token || !status.ilink_bot_id || !status.ilink_user_id) {
          throw new Error('QR login confirmed, but the API did not return bot credentials')
        }

        const credentials: Credentials = {
          token: status.bot_token,
          baseUrl: status.baseurl ?? baseUrl,
          accountId: status.ilink_bot_id,
          userId: status.ilink_user_id,
        }

        await saveCredentials(credentials, options.tokenPath)
        return credentials
      }

      if (status.status === 'expired') {
        break
      }

      await delay(QR_POLL_INTERVAL_MS)
    }
  }
}

export { DEFAULT_TOKEN_PATH }
