#!/usr/bin/env npx tsx
/**
 * 流式回复测试 v2
 *
 * 测试两种方式:
 * A) 同一 client_id + GENERATING → FINISH (协议的正规用法)
 * B) 不同 client_id + 每条都是 FINISH (对照组)
 *
 * 发 "A" 测试方式A，发 "B" 测试方式B，发其他走方式A
 */

import { WeixinBot, type IncomingMessage } from '@pinixai/weixin-bot'
import { randomBytes, randomUUID } from 'node:crypto'

const bot = new WeixinBot({
  onError: (err) => console.error('[ERROR]', err),
})

const creds = await bot.login()
console.log(`[INFO] 登录成功: ${creds.accountId}`)

function randomUin(): string {
  return Buffer.from(String(randomBytes(4).readUInt32BE(0)), 'utf8').toString('base64')
}

async function sendRaw(to: string, text: string, ctx: string, state: number, clientId: string) {
  const base = creds.baseUrl.replace(/\/+$/, '')
  const res = await fetch(`${base}/ilink/bot/sendmessage`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      AuthorizationType: 'ilink_bot_token',
      Authorization: `Bearer ${creds.token}`,
      'X-WECHAT-UIN': randomUin(),
    },
    body: JSON.stringify({
      msg: {
        from_user_id: '', to_user_id: to, client_id: clientId,
        message_type: 2, message_state: state, context_token: ctx,
        item_list: [{ type: 1, text_item: { text } }],
      },
      base_info: { channel_version: '1.0.0' },
    }),
  })
  return { ok: res.ok, body: await res.text() }
}

const sleep = (ms: number) => new Promise(r => setTimeout(r, ms))

bot.onMessage(async (msg: IncomingMessage) => {
  const mode = msg.text.trim().toUpperCase() === 'B' ? 'B' : 'A'
  console.log(`\n[RECV] "${msg.text}" → 模式 ${mode}`)

  if (mode === 'A') {
    // 方式A: 同一 client_id, GENERATING → GENERATING → FINISH
    const cid = randomUUID()
    console.log(`[A] client_id=${cid.slice(0, 8)}`)

    console.log('[A] 发送 GENERATING: "第一段..."')
    await sendRaw(msg.userId, '第一段...', msg._contextToken, 1, cid)
    await sleep(2000)

    console.log('[A] 发送 GENERATING: "第一段...第二段..."')
    await sendRaw(msg.userId, '第一段...第二段...', msg._contextToken, 1, cid)
    await sleep(2000)

    console.log('[A] 发送 FINISH: "第一段...第二段...完成！"')
    await sendRaw(msg.userId, '第一段...第二段...完成！', msg._contextToken, 2, cid)
    console.log('[A] 全部发送完毕')

  } else {
    // 方式B: 不同 client_id, 每条都是 FINISH (对照组)
    console.log('[B] 发送 FINISH #1')
    await sendRaw(msg.userId, '对照: 第一条', msg._contextToken, 2, randomUUID())
    await sleep(2000)

    console.log('[B] 发送 FINISH #2')
    await sendRaw(msg.userId, '对照: 第二条', msg._contextToken, 2, randomUUID())
    await sleep(2000)

    console.log('[B] 发送 FINISH #3')
    await sendRaw(msg.userId, '对照: 第三条', msg._contextToken, 2, randomUUID())
    console.log('[B] 全部发送完毕')
  }
})

console.log('[INFO] 流式测试 v2')
console.log('[INFO] 发 "A" → 测试 GENERATING→FINISH (同 client_id)')
console.log('[INFO] 发 "B" → 对照组 (每条独立 FINISH)')
console.log('[INFO] ────────────────────────────────────')
await bot.run()
