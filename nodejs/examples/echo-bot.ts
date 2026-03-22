import { WeixinBot } from '../src/index.js'

const bot = new WeixinBot()

await bot.login()

bot.onMessage(async (msg) => {
  console.log(`[${msg.timestamp.toLocaleTimeString()}] ${msg.userId}: ${msg.text}`)
  await bot.reply(msg, `你说了: ${msg.text}`)
})

console.log('Bot is running. Press Ctrl+C to stop.')
await bot.run()
