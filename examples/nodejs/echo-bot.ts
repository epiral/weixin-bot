#!/usr/bin/env npx tsx
/**
 * WeChat Echo Bot — 完整示例
 *
 * 功能：
 *   1. 首次运行扫码登录，凭证自动保存到 ~/.weixin-bot/credentials.json
 *   2. 后续运行自动加载已保存凭证，跳过扫码
 *   3. 收到消息后显示"正在输入"，然后回复 "Echo: ..."
 *   4. Session 过期时自动重新扫码
 *
 * 用法：
 *   npx tsx examples/nodejs/echo-bot.ts
 *   npx tsx examples/nodejs/echo-bot.ts --force-login   # 强制重新扫码
 */

import { WeixinBot } from '@pinixai/weixin-bot'

// ── 配置 ─────────────────────────────────────────────────────────────────

const forceLogin = process.argv.includes('--force-login')

// ── 日志工具 ──────────────────────────────────────────────────────────────

function log(level: string, msg: string) {
  const ts = new Date().toISOString()
  console.log(`${ts} [${level}] ${msg}`)
}

// ── 启动 ──────────────────────────────────────────────────────────────────

const bot = new WeixinBot({
  onError: (err) => {
    log('ERROR', err instanceof Error ? err.stack ?? err.message : String(err))
  },
})

// 登录
log('INFO', forceLogin ? '强制重新扫码登录...' : '正在登录（已有凭证则自动跳过扫码）...')
const creds = await bot.login({ force: forceLogin })
log('INFO', `登录成功 — Bot ID: ${creds.accountId}`)
log('INFO', `关联用户: ${creds.userId}`)
log('INFO', `API 地址: ${creds.baseUrl}`)

// 统计
let messageCount = 0
const startTime = Date.now()

// 注册消息处理
bot.onMessage(async (msg) => {
  messageCount++
  const elapsed = ((Date.now() - startTime) / 1000).toFixed(0)

  log('RECV', `#${messageCount} | 类型: ${msg.type} | 用户: ${msg.userId}`)
  log('RECV', `内容: ${msg.text}`)

  // 显示"对方正在输入中..."
  try {
    await bot.sendTyping(msg.userId)
    log('TYPING', '已发送 typing 状态')
  } catch {
    // typing 失败不影响回复
  }

  // 模拟处理耗时（实际场景中这里是调用 LLM 等）
  await new Promise((resolve) => setTimeout(resolve, 1000))

  const reply = `Echo: ${msg.text}`

  try {
    await bot.reply(msg, reply)
    log('SEND', `回复成功 (${reply.length} 字符) | 运行 ${elapsed}s | 累计 ${messageCount} 条`)
  } catch (err) {
    log('ERROR', `回复失败: ${err instanceof Error ? err.message : String(err)}`)
  }
})

// 优雅退出
process.on('SIGINT', () => {
  log('INFO', `收到 SIGINT，正在停止... (共处理 ${messageCount} 条消息)`)
  bot.stop()
})

process.on('SIGTERM', () => {
  log('INFO', `收到 SIGTERM，正在停止...`)
  bot.stop()
})

// 启动长轮询
log('INFO', '开始接收微信消息 (Ctrl+C 停止)')
log('INFO', '────────────────────────────────────')
await bot.run()

log('INFO', `Bot 已停止，共处理 ${messageCount} 条消息`)
