# @pinixai/weixin-bot

Zero-dependency Node.js SDK for the WeChat iLink Bot API.

## Install

```bash
npm install @pinixai/weixin-bot
```

## Quick start

```typescript
import { WeixinBot } from '@pinixai/weixin-bot'

const bot = new WeixinBot()
await bot.login()

bot.onMessage(async (msg) => {
  console.log(`[${msg.timestamp.toLocaleTimeString()}] ${msg.userId}: ${msg.text}`)
  await bot.reply(msg, `你说了: ${msg.text}`)
})

await bot.run()
```

## API reference

### `new WeixinBot(options?)`

Creates a bot client.

- `baseUrl?: string` Override the iLink API base URL.
- `tokenPath?: string` Override the credential file path. Default: `~/.weixin-bot/credentials.json`
- `onError?: (error: unknown) => void` Receive polling or handler errors.

### `await bot.login(options?)`

Starts QR login if needed, stores credentials locally, and returns the active session.

- `force?: boolean` Ignore cached credentials and require a fresh QR login.

### `bot.onMessage(handler)`

Registers an async or sync message handler. Each inbound user message is converted into:

```typescript
interface IncomingMessage {
  userId: string
  text: string
  type: 'text' | 'image' | 'voice' | 'file' | 'video'
  raw: WeixinMessage
  _contextToken: string
  timestamp: Date
}
```

### `await bot.reply(msg, text)`

Replies to an inbound message using that message's `context_token`.

### `await bot.send(userId, text)`

Sends a proactive text message using the latest cached `context_token` for that user. This only works after the SDK has seen at least one inbound message from that user.

### `await bot.run()`

Starts the long-poll loop, dispatches incoming messages to registered handlers, reconnects on transient failures, and triggers re-login if the session expires.

### `bot.stop()`

Stops the long-poll loop gracefully.

## How it works

1. `login()` fetches a QR login URL, waits for WeChat confirmation, and saves the returned bot token.
2. `run()` performs long polling against `getupdates`.
3. Each inbound message is normalized into `IncomingMessage` and sent to your callbacks.
4. `reply()` and `send()` reuse the internally managed `context_token` required by the protocol.

## Protocol

See [../PROTOCOL.md](../PROTOCOL.md) for the wire protocol reference used by this SDK.

## License

MIT
