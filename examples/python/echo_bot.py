#!/usr/bin/env python3
"""
WeChat Echo Bot — 完整示例

用法：
  uv run echo_bot.py
  uv run echo_bot.py --force-login
"""
from __future__ import annotations

import sys
import time

from weixin_bot import WeixinBot
from weixin_bot.auth import login as _sdk_login, load_credentials, Credentials

# ── 带 QR 渲染的登录 ─────────────────────────────────────────────────────

async def _login_with_qr(base_url: str, token_path: str | None, force: bool) -> Credentials:
    """Wrap SDK login to render QR code in terminal."""
    import asyncio
    from weixin_bot.api import DEFAULT_BASE_URL, fetch_qr_code, poll_qr_status

    if not force:
        creds = await load_credentials(token_path)
        if creds is not None:
            return creds

    base = base_url or DEFAULT_BASE_URL

    while True:
        qr = await fetch_qr_code(base)
        url = qr["qrcode_img_content"]

        # Render QR
        try:
            import qrcode as qrlib
            q = qrlib.QRCode(border=2)
            q.add_data(url)
            q.make(fit=True)
            matrix = q.get_matrix()
            rows = len(matrix)
            for y in range(0, rows, 2):
                line = []
                for x in range(len(matrix[0])):
                    top = matrix[y][x]
                    bot = matrix[y + 1][x] if y + 1 < rows else False
                    if top and bot: line.append("█")
                    elif top: line.append("▀")
                    elif bot: line.append("▄")
                    else: line.append(" ")
                sys.stderr.write("".join(line) + "\n")
        except ImportError:
            pass

        sys.stderr.write(f"[weixin-bot] 用微信扫描上方二维码，或在微信中打开以下链接:\n{url}\n")

        last_status = None
        while True:
            status = await poll_qr_status(base, qr["qrcode"])
            if status["status"] != last_status:
                if status["status"] == "scaned":
                    sys.stderr.write("[weixin-bot] 已扫码，请在微信确认...\n")
                elif status["status"] == "expired":
                    sys.stderr.write("[weixin-bot] 二维码已过期，重新获取...\n")
                last_status = status["status"]

            if status["status"] == "confirmed":
                from weixin_bot.auth import Credentials as C, _save_credentials_sync
                import asyncio
                creds = C(
                    token=status["bot_token"],
                    base_url=status.get("baseurl") or base,
                    account_id=status["ilink_bot_id"],
                    user_id=status["ilink_user_id"],
                )
                await asyncio.to_thread(_save_credentials_sync, creds, token_path)
                sys.stderr.write("[weixin-bot] 登录成功!\n")
                return creds

            if status["status"] == "expired":
                break

            await asyncio.sleep(2)

# ── 日志 ──────────────────────────────────────────────────────────────────

def log(level: str, msg: str) -> None:
    ts = time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime())
    print(f"{ts} [{level}] {msg}")

# ── 启动 ──────────────────────────────────────────────────────────────────

import asyncio

force_login = "--force-login" in sys.argv

bot = WeixinBot(on_error=lambda err: log("ERROR", str(err)))

# 使用自定义登录（带 QR 渲染）
log("INFO", "强制重新扫码登录..." if force_login else "正在登录...")
creds = asyncio.run(_login_with_qr(bot._base_url, bot._token_path, force_login))
bot._credentials = creds
bot._base_url = creds.base_url
log("INFO", f"登录成功 — Bot ID: {creds.account_id}")
log("INFO", f"关联用户: {creds.user_id}")
log("INFO", f"API 地址: {creds.base_url}")

message_count = 0
start_time = time.time()


@bot.on_message
async def handle(msg):
    global message_count
    message_count += 1
    elapsed = int(time.time() - start_time)

    log("RECV", f"#{message_count} | 类型: {msg.type} | 用户: {msg.user_id}")
    log("RECV", f"内容: {msg.text}")

    try:
        await bot.send_typing(msg.user_id)
    except Exception:
        pass

    reply = f"Echo: {msg.text}"

    try:
        await bot.reply(msg, reply)
        log("SEND", f"回复成功 ({len(reply)} 字符) | 运行 {elapsed}s | 累计 {message_count} 条")
    except Exception as err:
        log("ERROR", f"回复失败: {err}")


log("INFO", "开始接收微信消息 (Ctrl+C 停止)")
log("INFO", "────────────────────────────────────")

try:
    bot.run()
except KeyboardInterrupt:
    log("INFO", f"Bot 已停止，共处理 {message_count} 条消息")
