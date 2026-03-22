export interface BaseInfo {
  channel_version: string
}

export enum MessageType {
  USER = 1,
  BOT = 2,
}

export enum MessageState {
  NEW = 0,
  GENERATING = 1,
  FINISH = 2,
}

export enum MessageItemType {
  TEXT = 1,
  IMAGE = 2,
  VOICE = 3,
  FILE = 4,
  VIDEO = 5,
}

export interface CDNMedia {
  encrypt_query_param: string
  aes_key: string
  encrypt_type?: 0 | 1
}

export interface TextItem {
  text: string
}

export interface ImageItem {
  media: CDNMedia
  aeskey?: string
  url?: string
  mid_size?: string | number
  thumb_size?: string | number
  thumb_height?: number
  thumb_width?: number
  hd_size?: string | number
}

export interface VoiceItem {
  media: CDNMedia
  encode_type?: number
  text?: string
  playtime?: number
}

export interface FileItem {
  media: CDNMedia
  file_name?: string
  md5?: string
  len?: string
}

export interface VideoItem {
  media: CDNMedia
  video_size?: string | number
  play_length?: number
  thumb_media?: CDNMedia
}

export interface RefMessage {
  title?: string
  message_item?: MessageItem
}

export interface MessageItem {
  type: MessageItemType
  text_item?: TextItem
  image_item?: ImageItem
  voice_item?: VoiceItem
  file_item?: FileItem
  video_item?: VideoItem
  ref_msg?: RefMessage
}

export interface WeixinMessage {
  message_id: number
  from_user_id: string
  to_user_id: string
  client_id: string
  create_time_ms: number
  message_type: MessageType
  message_state: MessageState
  context_token: string
  item_list: MessageItem[]
}

export interface GetUpdatesReq {
  get_updates_buf: string
  base_info: BaseInfo
}

export interface GetUpdatesResp {
  ret: number
  msgs: WeixinMessage[]
  get_updates_buf: string
  longpolling_timeout_ms?: number
  errcode?: number
  errmsg?: string
}

export interface SendMessageReq {
  msg: {
    from_user_id: string
    to_user_id: string
    client_id: string
    message_type: MessageType
    message_state: MessageState
    context_token: string
    item_list: MessageItem[]
  }
  base_info: BaseInfo
}

export interface SendTypingReq {
  ilink_user_id: string
  typing_ticket: string
  status: 1 | 2
  base_info: BaseInfo
}

export interface GetConfigResp {
  typing_ticket?: string
  ret?: number
  errcode?: number
  errmsg?: string
}

export interface IncomingMessage {
  userId: string
  text: string
  type: 'text' | 'image' | 'voice' | 'file' | 'video'
  raw: WeixinMessage
  /** context_token, managed internally */
  _contextToken: string
  timestamp: Date
}
