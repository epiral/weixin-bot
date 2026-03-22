# Node.js Echo Bot 示例

收到微信消息后原样回复，带 typing 状态和终端二维码渲染。

## 运行

```bash
cd examples/nodejs
npm install
npx tsx echo-bot.ts
```

首次运行会显示二维码，用微信扫码登录。凭证保存在 `~/.weixin-bot/credentials.json`，后续运行自动跳过扫码。

### 强制重新登录

```bash
npx tsx echo-bot.ts --force-login
```

## 文件说明

| 文件 | 说明 |
|---|---|
| `echo-bot.ts` | 完整示例：登录 → 收消息 → typing → 回复 |
| `stream-test.ts` | GENERATING vs FINISH 流式测试 |
| `generating-test.ts` | sendtyping vs GENERATING 对比测试 |

## 依赖

- `@pinixai/weixin-bot` — SDK
- `qrcode-terminal` — 终端二维码渲染（仅 example 使用，SDK 不依赖）
- `tsx` — 直接运行 TypeScript
