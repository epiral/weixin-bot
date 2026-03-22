# WeChat iLink Bot API Protocol Reference

Base URL: `https://ilinkai.weixin.qq.com`
CDN URL: `https://novac2c.cdn.weixin.qq.com/c2c`

## Authentication (QR Login)

### Step 1: Get QR Code
```
GET /ilink/bot/get_bot_qrcode?bot_type=3
→ { qrcode: "<token>", qrcode_img_content: "<url>" }
```

### Step 2: Poll Status
```
GET /ilink/bot/get_qrcode_status?qrcode=<token>
Headers: { "iLink-App-ClientVersion": "1" }
→ { status: "wait" | "scaned" | "confirmed" | "expired", bot_token?, ilink_bot_id?, ilink_user_id?, baseurl? }
```

## Common Headers (all POST requests)
```
Content-Type: application/json
AuthorizationType: ilink_bot_token
Authorization: Bearer <bot_token>
X-WECHAT-UIN: <base64(String(randomUint32))>
```
All POST bodies include: `base_info: { channel_version: "<version>" }`

## Get Updates (Long Poll)
```
POST /ilink/bot/getupdates
Body: { get_updates_buf: "<cursor_or_empty>", base_info: {...} }
Timeout: 35s (server holds connection)
→ { ret: 0, msgs: WeixinMessage[], get_updates_buf: "<new_cursor>", longpolling_timeout_ms?: number }
```
Error: ret != 0, errcode -14 = session expired (re-login needed)

## Send Message
```
POST /ilink/bot/sendmessage
Body: {
  msg: {
    from_user_id: "",
    to_user_id: "<user_id>",
    client_id: "<unique_id>",
    message_type: 2,        // BOT
    message_state: 2,       // FINISH
    context_token: "<from_inbound_msg>",  // REQUIRED
    item_list: [{ type: 1, text_item: { text: "..." } }]
  },
  base_info: {...}
}
```

## Send Typing
```
POST /ilink/bot/sendtyping
Body: { ilink_user_id: "<id>", typing_ticket: "<ticket>", status: 1|2, base_info: {...} }
```
Get ticket via: `POST /ilink/bot/getconfig { ilink_user_id, context_token, base_info }`
→ `{ typing_ticket: "<base64>" }`

## Get Upload URL (for media)
```
POST /ilink/bot/getuploadurl
Body: { filekey, media_type(1=IMG,2=VID,3=FILE,4=VOICE), to_user_id, rawsize, rawfilemd5, filesize, aeskey, no_need_thumb: true, base_info }
→ { upload_param: "<encrypted>" }
```

## CDN Upload
```
POST <cdn_base>/upload?encrypted_query_param=<upload_param>&filekey=<key>
Content-Type: application/octet-stream
Body: AES-128-ECB encrypted file bytes
Response Header: x-encrypted-param → download param
```

## CDN Download
```
GET <cdn_base>/download?encrypted_query_param=<param>
→ AES-128-ECB encrypted bytes, decrypt with aes_key
```

## Message Types
```typescript
WeixinMessage {
  message_id: number, from_user_id: string, to_user_id: string,
  client_id: string, create_time_ms: number,
  message_type: 1(USER) | 2(BOT),
  message_state: 0(NEW) | 1(GENERATING) | 2(FINISH),
  context_token: string,  // MUST echo back in replies
  item_list: MessageItem[]
}

MessageItem {
  type: 1(TEXT) | 2(IMAGE) | 3(VOICE) | 4(FILE) | 5(VIDEO)
  text_item?: { text: string }
  image_item?: { media: CDNMedia, aeskey?: string, url?: string, mid_size?, thumb_size?, thumb_height?, thumb_width?, hd_size? }
  voice_item?: { media: CDNMedia, encode_type?: number, text?: string, playtime?: number }
  file_item?: { media: CDNMedia, file_name?: string, md5?: string, len?: string }
  video_item?: { media: CDNMedia, video_size?, play_length?, thumb_media?: CDNMedia }
  ref_msg?: { title?: string, message_item?: MessageItem }
}

CDNMedia { encrypt_query_param: string, aes_key: string, encrypt_type?: 0|1 }
```

## Crypto
- AES-128-ECB with PKCS7 padding
- aes_key encoding: base64(raw 16 bytes) for images, base64(hex 32 chars) for file/voice/video
- Upload: generate random 16-byte key, encrypt file, POST to CDN
- Download: fetch from CDN, decrypt with key from CDNMedia
