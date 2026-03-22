# Python Echo Bot 示例

收到微信消息后原样回复，带 typing 状态和终端二维码渲染。

## 运行

```bash
cd examples/python
uv run echo_bot.py
```

首次运行会显示二维码，用微信扫码登录。凭证保存在 `~/.weixin-bot/credentials.json`，后续运行自动跳过扫码。

### 强制重新登录

```bash
uv run echo_bot.py --force-login
```

### 不用 uv

```bash
cd examples/python
pip install weixin-bot-sdk qrcode
python echo_bot.py
```

## 文件说明

| 文件 | 说明 |
|---|---|
| `echo_bot.py` | 完整示例：登录 → 收消息 → typing → 回复 |

## 依赖

- `weixin-bot-sdk` — SDK
- `qrcode` — 终端二维码渲染（仅 example 使用，SDK 不依赖）

## 要求

- Python 3.11+
