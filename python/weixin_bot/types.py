from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import IntEnum
from typing import TYPE_CHECKING, Literal, TypedDict

if TYPE_CHECKING:
    from typing import NotRequired, TypeAlias


class MessageType(IntEnum):
    USER = 1
    BOT = 2


class MessageState(IntEnum):
    NEW = 0
    GENERATING = 1
    FINISH = 2


class MessageItemType(IntEnum):
    TEXT = 1
    IMAGE = 2
    VOICE = 3
    FILE = 4
    VIDEO = 5


class BaseInfo(TypedDict):
    channel_version: str


class CDNMedia(TypedDict):
    encrypt_query_param: str
    aes_key: str
    encrypt_type: NotRequired[int]


class TextItem(TypedDict):
    text: str


class ImageItem(TypedDict):
    media: CDNMedia
    aeskey: NotRequired[str]
    url: NotRequired[str]
    mid_size: NotRequired[str | int]
    thumb_size: NotRequired[str | int]
    thumb_height: NotRequired[int]
    thumb_width: NotRequired[int]
    hd_size: NotRequired[str | int]


class VoiceItem(TypedDict):
    media: CDNMedia
    encode_type: NotRequired[int]
    text: NotRequired[str]
    playtime: NotRequired[int]


class FileItem(TypedDict):
    media: CDNMedia
    file_name: NotRequired[str]
    md5: NotRequired[str]
    len: NotRequired[str]


class VideoItem(TypedDict):
    media: CDNMedia
    video_size: NotRequired[str | int]
    play_length: NotRequired[int]
    thumb_media: NotRequired[CDNMedia]


class RefMessage(TypedDict):
    title: NotRequired[str]
    message_item: NotRequired[MessageItem]


class MessageItem(TypedDict):
    type: MessageItemType
    text_item: NotRequired[TextItem]
    image_item: NotRequired[ImageItem]
    voice_item: NotRequired[VoiceItem]
    file_item: NotRequired[FileItem]
    video_item: NotRequired[VideoItem]
    ref_msg: NotRequired[RefMessage]


class WeixinMessage(TypedDict):
    message_id: int
    from_user_id: str
    to_user_id: str
    client_id: str
    create_time_ms: int
    message_type: MessageType
    message_state: MessageState
    context_token: str
    item_list: list[MessageItem]


class GetUpdatesRequest(TypedDict):
    get_updates_buf: str
    base_info: BaseInfo


class GetUpdatesResponse(TypedDict):
    ret: int
    msgs: list[WeixinMessage]
    get_updates_buf: str
    longpolling_timeout_ms: NotRequired[int]
    errcode: NotRequired[int]
    errmsg: NotRequired[str]


class SendMessageMessage(TypedDict):
    from_user_id: str
    to_user_id: str
    client_id: str
    message_type: MessageType
    message_state: MessageState
    context_token: str
    item_list: list[MessageItem]


class SendMessageRequest(TypedDict):
    msg: SendMessageMessage
    base_info: BaseInfo


class SendTypingRequest(TypedDict):
    ilink_user_id: str
    typing_ticket: str
    status: Literal[1, 2]
    base_info: BaseInfo


class GetConfigResponse(TypedDict):
    typing_ticket: NotRequired[str]
    ret: NotRequired[int]
    errcode: NotRequired[int]
    errmsg: NotRequired[str]


class QrCodeResponse(TypedDict):
    qrcode: str
    qrcode_img_content: str


class QrStatusResponse(TypedDict):
    status: Literal["wait", "scaned", "confirmed", "expired"]
    bot_token: NotRequired[str]
    ilink_bot_id: NotRequired[str]
    ilink_user_id: NotRequired[str]
    baseurl: NotRequired[str]


MessageKind: TypeAlias = Literal["text", "image", "voice", "file", "video"]


@dataclass
class IncomingMessage:
    user_id: str
    text: str
    type: MessageKind
    raw: WeixinMessage
    _context_token: str
    timestamp: datetime


__all__ = [
    "BaseInfo",
    "CDNMedia",
    "FileItem",
    "GetConfigResponse",
    "GetUpdatesRequest",
    "GetUpdatesResponse",
    "ImageItem",
    "IncomingMessage",
    "MessageItem",
    "MessageItemType",
    "MessageKind",
    "MessageState",
    "MessageType",
    "QrCodeResponse",
    "QrStatusResponse",
    "RefMessage",
    "SendMessageMessage",
    "SendMessageRequest",
    "SendTypingRequest",
    "TextItem",
    "VideoItem",
    "VoiceItem",
    "WeixinMessage",
]
