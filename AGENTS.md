# Project: @pinix/weixin-bot

WeChat iLink Bot SDK for Node.js. Wraps the WeChat iLink Bot API into a simple event-driven API.

## Structure
```
nodejs/
├── src/
│   ├── index.ts        ← re-exports
│   ├── client.ts       ← WeixinBot class (main entry)
│   ├── auth.ts         ← QR login flow
│   ├── types.ts        ← TypeScript types for API
│   └── api.ts          ← low-level HTTP helpers
├── examples/
│   └── echo-bot.ts     ← minimal example
├── package.json
├── tsconfig.json
└── README.md
```

## Key Design Decisions
- Package name: `@pinix/weixin-bot`
- Zero runtime dependencies (uses Node.js built-in fetch, crypto)
- One dev dependency: typescript
- Target: Node.js >= 22 (for native fetch)
- Build: tsc → dist/
- Credentials stored at `~/.weixin-bot/credentials.json`
- context_token managed internally by SDK (user never sees it)
- API style: event-driven with `.on('message', handler)`

## Protocol Reference
See PROTOCOL.md in repo root for full API spec.
