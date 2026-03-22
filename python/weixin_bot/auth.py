from __future__ import annotations

import asyncio
import json
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .api import DEFAULT_BASE_URL, fetch_qr_code, poll_qr_status

DEFAULT_TOKEN_DIR = Path.home() / ".weixin-bot"
DEFAULT_TOKEN_PATH = DEFAULT_TOKEN_DIR / "credentials.json"
QR_POLL_INTERVAL_MS = 2_000


@dataclass
class Credentials:
    token: str
    base_url: str
    account_id: str
    user_id: str


def _resolve_token_path(token_path: str | Path | None) -> Path:
    return Path(token_path) if token_path is not None else DEFAULT_TOKEN_PATH


def _log(message: str) -> None:
    sys.stderr.write(f"[weixin-bot] {message}\n")


def _save_credentials_sync(credentials: Credentials, token_path: str | Path | None) -> None:
    target_path = _resolve_token_path(token_path)
    target_path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    payload = {
        "token": credentials.token,
        "baseUrl": credentials.base_url,
        "accountId": credentials.account_id,
        "userId": credentials.user_id,
    }
    target_path.write_text(f"{json.dumps(payload, indent=2)}\n", encoding="utf-8")
    target_path.chmod(0o600)


def _load_credentials_sync(token_path: str | Path | None) -> Credentials | None:
    target_path = _resolve_token_path(token_path)

    try:
        parsed = json.loads(target_path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return None

    return _coerce_credentials(parsed, target_path)


def _coerce_credentials(value: Any, source: Path) -> Credentials:
    if not isinstance(value, dict):
        raise ValueError(f"Invalid credentials format in {source}")

    token = value.get("token")
    base_url = value.get("base_url")
    account_id = value.get("account_id")
    user_id = value.get("user_id")
    legacy_base_url = value.get("baseUrl")
    legacy_account_id = value.get("accountId")
    legacy_user_id = value.get("userId")

    resolved_base_url = base_url if isinstance(base_url, str) else legacy_base_url
    resolved_account_id = account_id if isinstance(account_id, str) else legacy_account_id
    resolved_user_id = user_id if isinstance(user_id, str) else legacy_user_id

    if not isinstance(token, str) or not isinstance(resolved_base_url, str) or not isinstance(resolved_account_id, str) or not isinstance(resolved_user_id, str):
        raise ValueError(f"Invalid credentials format in {source}")

    return Credentials(
        token=token,
        base_url=resolved_base_url,
        account_id=resolved_account_id,
        user_id=resolved_user_id,
    )


def _clear_credentials_sync(token_path: str | Path | None) -> None:
    _resolve_token_path(token_path).unlink(missing_ok=True)


def _print_qr_instructions(url: str) -> None:
    import qrcode

    try:
        qr = qrcode.QRCode(border=1)
        qr.add_data(url)
        qr.make(fit=True)
        qr.print_ascii(tty=False)
        print("请使用微信扫描上方二维码完成登录。")
    except Exception:
        _log("在微信中打开以下链接完成登录:")
        sys.stderr.write(f"{url}\n")


async def load_credentials(token_path: str | Path | None = None) -> Credentials | None:
    return await asyncio.to_thread(_load_credentials_sync, token_path)


async def clear_credentials(token_path: str | Path | None = None) -> None:
    await asyncio.to_thread(_clear_credentials_sync, token_path)


async def login(
    base_url: str = DEFAULT_BASE_URL,
    token_path: str | Path | None = None,
    force: bool = False,
) -> Credentials:
    if not force:
        existing = await load_credentials(token_path)
        if existing is not None:
            return existing

    while True:
        qr = await fetch_qr_code(base_url)
        _print_qr_instructions(qr["qrcode_img_content"])

        last_status: str | None = None

        while True:
            status = await poll_qr_status(base_url, qr["qrcode"])

            if status["status"] != last_status:
                if status["status"] == "scaned":
                    _log("QR code scanned. Confirm the login inside WeChat.")
                elif status["status"] == "confirmed":
                    _log("Login confirmed.")
                elif status["status"] == "expired":
                    _log("QR code expired. Requesting a new one...")
                last_status = status["status"]

            if status["status"] == "confirmed":
                token = status.get("bot_token")
                account_id = status.get("ilink_bot_id")
                user_id = status.get("ilink_user_id")
                if not isinstance(token, str) or not isinstance(account_id, str) or not isinstance(user_id, str):
                    raise RuntimeError("QR login confirmed, but the API did not return bot credentials")

                credentials = Credentials(
                    token=token,
                    base_url=status.get("baseurl") or base_url,
                    account_id=account_id,
                    user_id=user_id,
                )
                await asyncio.to_thread(_save_credentials_sync, credentials, token_path)
                return credentials

            if status["status"] == "expired":
                break

            await asyncio.sleep(QR_POLL_INTERVAL_MS / 1000)


__all__ = [
    "Credentials",
    "DEFAULT_TOKEN_PATH",
    "clear_credentials",
    "load_credentials",
    "login",
]
