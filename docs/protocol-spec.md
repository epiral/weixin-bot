# 微信 iLink Bot API 协议技术文档

本文基于仓库根目录的 `PROTOCOL.md` 摘要，以及上游实现中的实际类型定义、HTTP 封装、登录流程、消息收发、CDN 上传下载和 session 管理逻辑整理而成。文档目标不是描述某个 UI，而是给 SDK / 客户端实现者一份可直接落地的线协议说明。

## 1. 概述

微信 iLink Bot API 是一套“HTTP JSON 控制面 + CDN 二进制媒体面”的协议：

- 登录通过二维码完成。
- 消息接收通过 `getupdates` 长轮询完成。
- 消息发送通过 `sendmessage` 完成。
- Typing 状态通过 `getconfig` + `sendtyping` 完成。
- 媒体文件通过 `getuploadurl` 获取上传参数，再走微信 CDN 上传/下载。

默认地址如下：

- API Base URL: `https://ilinkai.weixin.qq.com`
- CDN Base URL: `https://novac2c.cdn.weixin.qq.com/c2c`

核心特点如下：

- 认证结果是 `bot_token`，后续所有 CGI `POST` 请求都用 `Authorization: Bearer ...`。
- 所有 CGI `POST` 请求都携带 `base_info`，当前已观测到的字段只有 `channel_version`。
- 入站消息里会带 `context_token`，回复时必须原样回传，否则消息无法挂到正确会话上下文。
- 媒体文件使用 AES-128-ECB + PKCS7 填充加密，CDN 上传和下载都围绕 `encrypt_query_param` / `upload_param` 这类 opaque token 运作。
- 登录完成时服务端会返回 `baseurl`，后续请求应优先使用该值，而不是硬编码默认 API Base URL。

当前实现中固定使用的 `bot_type` 是 `3`。

## 2. 认证流程

### 2.1 获取二维码

请求：

```bash
curl 'https://ilinkai.weixin.qq.com/ilink/bot/get_bot_qrcode?bot_type=3'
```

典型响应：

```json
{
  "qrcode": "qr_3_1761134400_5f82c8d90b0d4a34",
  "qrcode_img_content": "https://weixin.qq.com/x/AbCdEfGhIjKlMnOpQrSt"
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `qrcode` | string | 二维码轮询 token，后续传给 `get_qrcode_status`。 |
| `qrcode_img_content` | string | 可直接展示给用户的二维码内容，实际观测中是一个 URL。 |

### 2.2 轮询二维码状态

请求：

```bash
curl --max-time 40 \
  -H 'iLink-App-ClientVersion: 1' \
  'https://ilinkai.weixin.qq.com/ilink/bot/get_qrcode_status?qrcode=qr_3_1761134400_5f82c8d90b0d4a34'
```

`status=wait` 响应：

```json
{
  "status": "wait"
}
```

`status=scaned` 响应：

```json
{
  "status": "scaned"
}
```

注意，服务端状态值拼写就是 `scaned`，不是 `scanned`。

`status=confirmed` 响应：

```json
{
  "status": "confirmed",
  "bot_token": "eyJhbGciOiJIUzI1NiJ9.ilink_bot_demo.2wYH0XxJqO9iN0E4x1Q7FA",
  "ilink_bot_id": "b0f5860fdecb@im.bot",
  "ilink_user_id": "wxid_7h3d8k2p9q@im.wechat",
  "baseurl": "https://ilinkai.weixin.qq.com"
}
```

`status=expired` 响应：

```json
{
  "status": "expired"
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `status` | string | 取值为 `wait` / `scaned` / `confirmed` / `expired`。 |
| `bot_token` | string | 登录成功后返回的 Bearer token。 |
| `ilink_bot_id` | string | Bot 账号 ID，后续可作为账户主键。 |
| `ilink_user_id` | string | 扫码确认的微信用户 ID。 |
| `baseurl` | string | 后续 CGI 请求应优先使用的 API Base URL。 |

### 2.3 完整登录时序

推荐客户端流程如下：

1. 调用 `GET /ilink/bot/get_bot_qrcode?bot_type=3` 获取二维码。
2. 用 `GET /ilink/bot/get_qrcode_status?qrcode=...` 长轮询状态。
3. `status=wait` 时继续轮询。
4. `status=scaned` 时提示“已扫码，等待微信端确认”。
5. `status=expired` 时重新调用 `get_bot_qrcode` 获取新二维码。
6. `status=confirmed` 时保存 `bot_token`、`ilink_bot_id`、`ilink_user_id`、`baseurl`。

实现侧补充说明：

- 当前上游实现在二维码状态轮询上使用 35 秒客户端超时；如果本地超时，会把这次轮询当作 `wait` 处理并继续请求。
- 如果 `status=confirmed` 但响应里没有 `ilink_bot_id`，实现会把这次登录视为失败。
- 当前登录实现会在二维码多次过期时自动刷新二维码，最多刷新 3 次。

## 3. 公共请求规范

### 3.1 适用范围

以下 CGI `POST` 接口遵循统一请求格式：

- `POST /ilink/bot/getupdates`
- `POST /ilink/bot/sendmessage`
- `POST /ilink/bot/getconfig`
- `POST /ilink/bot/sendtyping`
- `POST /ilink/bot/getuploadurl`

二维码相关 `GET` 接口不使用 `Authorization`。CDN 上传下载也不使用这套 Header，而是通过 URL 中的加密参数授权。

### 3.2 公共 Headers

| Header | 是否必需 | 说明 |
| --- | --- | --- |
| `Content-Type: application/json` | 是 | 所有 CGI `POST` 都发 JSON。 |
| `AuthorizationType: ilink_bot_token` | 是 | 固定字面量。 |
| `Authorization: Bearer <bot_token>` | 是 | 登录成功后拿到的 token。 |
| `Content-Length` | 是 | JSON body 的 UTF-8 字节长度；`curl` 会自动生成。 |
| `X-WECHAT-UIN` | 是 | 每个请求都重新生成。 |
| `SKRouteTag` | 否 | 可选路由 Header，来自上层配置；协议本身不依赖它的语义。 |

一个完整的原始请求头示例如下：

```http
POST /ilink/bot/getupdates HTTP/1.1
Host: ilinkai.weixin.qq.com
Content-Type: application/json
AuthorizationType: ilink_bot_token
Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.ilink_bot_demo.2wYH0XxJqO9iN0E4x1Q7FA
X-WECHAT-UIN: MzA1NDE5ODk2
Content-Length: 111
```

### 3.3 `base_info`

所有 CGI `POST` body 都要带 `base_info`：

```json
{
  "base_info": {
    "channel_version": "1.0.0"
  }
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `channel_version` | string | 客户端/SDK 的版本号。当前实现从 `package.json` 读取；读取失败时会退化为 `"unknown"`。 |

### 3.4 `X-WECHAT-UIN` 生成算法

算法非常具体：

1. 生成一个随机 `uint32`。
2. 把它转成十进制字符串。
3. 对该十进制字符串的 UTF-8 字节做 base64 编码。

示例：

- 随机整数：`305419896`
- 十进制字符串：`"305419896"`
- Header 值：`MzA1NDE5ODk2`

也就是说，`X-WECHAT-UIN` 不是二进制整数的 base64，而是“十进制文本”的 base64。

### 3.5 bytes 字段编码规则

上游类型定义明确表明，proto 里的 bytes 字段在 JSON 中都以 base64 字符串序列化。已观测到的典型字段包括：

- `get_updates_buf`
- `sync_buf`（兼容字段，已废弃）
- `typing_ticket`
- `CDNMedia.aes_key`

而 `upload_param`、`encrypt_query_param` 这类字段虽然看起来也像 base64，但在协议层应视为 opaque string，不要自行解码或改写。

## 4. 消息接收 `getupdates`

### 4.1 接口定义

- 方法：`POST`
- 路径：`/ilink/bot/getupdates`
- 作用：长轮询拉取新的 `WeixinMessage` 列表，并更新游标 `get_updates_buf`

请求体：

```json
{
  "get_updates_buf": "eyJsYXN0X3NlcSI6MTI4LCJsYXN0X21zZ19pZCI6OTg3NjU0MzIxfQ==",
  "base_info": {
    "channel_version": "1.0.0"
  }
}
```

`curl` 示例：

```bash
curl --max-time 40 \
  'https://ilinkai.weixin.qq.com/ilink/bot/getupdates' \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'AuthorizationType: ilink_bot_token' \
  -H 'Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.ilink_bot_demo.2wYH0XxJqO9iN0E4x1Q7FA' \
  -H 'X-WECHAT-UIN: MzA1NDE5ODk2' \
  --data-raw '{
    "get_updates_buf": "eyJsYXN0X3NlcSI6MTI4LCJsYXN0X21zZ19pZCI6OTg3NjU0MzIxfQ==",
    "base_info": {
      "channel_version": "1.0.0"
    }
  }'
```

典型成功响应：

```json
{
  "ret": 0,
  "msgs": [
    {
      "seq": 129,
      "message_id": 987654322,
      "from_user_id": "wxid_7h3d8k2p9q@im.wechat",
      "to_user_id": "b0f5860fdecb@im.bot",
      "client_id": "wx-msg-1761134400123-a13f9b72",
      "create_time_ms": 1761134400123,
      "update_time_ms": 1761134400123,
      "session_id": "sess_1761134400_d37f2c1e",
      "message_type": 1,
      "message_state": 2,
      "context_token": "Y3R4LXd4aWRfN2gzZDhrMnA5cS0xNzYxMTM0NDAwMDAwLTJhNDcwYTk5",
      "item_list": [
        {
          "type": 1,
          "text_item": {
            "text": "帮我总结一下这张图"
          }
        }
      ]
    }
  ],
  "get_updates_buf": "eyJsYXN0X3NlcSI6MTI5LCJsYXN0X21zZ19pZCI6OTg3NjU0MzIyfQ==",
  "longpolling_timeout_ms": 35000
}
```

### 4.2 请求字段

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `get_updates_buf` | string | 是 | 上次成功拉取后保存的游标；没有时传空字符串 `""`。 |
| `base_info` | object | 是 | 公共请求元信息。 |

### 4.3 响应字段

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `ret` | number | 业务返回值；`0` 表示成功。 |
| `errcode` | number | 出错时的错误码；`-14` 已明确表示 session 过期。 |
| `errmsg` | string | 错误描述。 |
| `msgs` | `WeixinMessage[]` | 新消息列表。 |
| `get_updates_buf` | string | 下一次请求要回传的游标。 |
| `sync_buf` | string | 兼容字段，已废弃。 |
| `longpolling_timeout_ms` | number | 服务端建议的下一次长轮询超时时间。 |

### 4.4 long-poll 机制

`getupdates` 是标准长轮询：

- 服务端可能会 hold 住请求，直到有新消息或超时。
- 当前实现默认把客户端超时设为 35 秒。
- 如果响应里带了 `longpolling_timeout_ms`，下一次轮询应采用该值。
- 如果客户端本地 35 秒超时但没有收到任何响应，这通常是“空轮询”，不是异常。

当前实现对“本地超时”的处理是返回一个逻辑成功值：

```json
{
  "ret": 0,
  "msgs": [],
  "get_updates_buf": "eyJsYXN0X3NlcSI6MTI4LCJsYXN0X21zZ19pZCI6OTg3NjU0MzIxfQ=="
}
```

也就是说，客户端可以直接带着原来的游标继续下一轮请求。

### 4.5 游标管理

`get_updates_buf` 是一个 opaque 的上下文缓存，推荐策略如下：

1. 第一次拉取时发送空字符串 `""`。
2. 每次成功返回后，如果响应里有非空 `get_updates_buf`，立刻持久化。
3. 下一次轮询时把刚保存的值原样回传。
4. 如果本地游标损坏、缓存丢失或你明确要重置消费位置，再改回 `""`。

上游实现按账号持久化该值，说明这个游标应该和 `ilink_bot_id` 一一对应，而不是全局共享。

### 4.6 错误处理

当前实现把以下情况都当成失败：

- `ret` 存在且不等于 `0`
- `errcode` 存在且不等于 `0`

处理策略如下：

- `errcode = -14` 或 `ret = -14`：视为 session 过期，进入第 9 节的暂停与重连流程。
- 其他业务错误：短暂重试。
- 连续失败达到 3 次：退避 30 秒后再继续轮询。

## 5. 消息数据结构

### 5.1 `WeixinMessage`

`getupdates` 和 `sendmessage` 都围绕同一个统一消息结构 `WeixinMessage`：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `seq` | number | 消息序号，主要用于排序/同步。 |
| `message_id` | number | 平台消息 ID。 |
| `from_user_id` | string | 发送方 ID。 |
| `to_user_id` | string | 接收方 ID。 |
| `client_id` | string | 客户端侧唯一 ID；发送消息时由客户端生成。 |
| `create_time_ms` | number | 创建时间，毫秒时间戳。 |
| `update_time_ms` | number | 更新时间，毫秒时间戳。 |
| `delete_time_ms` | number | 删除时间，毫秒时间戳。 |
| `session_id` | string | 会话 ID。 |
| `group_id` | string | 群聊 ID；当前 direct channel 中通常为空。 |
| `message_type` | number | 消息方向/来源类型。 |
| `message_state` | number | 消息状态。 |
| `item_list` | `MessageItem[]` | 消息内容项数组。 |
| `context_token` | string | 关联上下文 token，回复时必须回传。 |

枚举值：

| 枚举 | 数值 | 说明 |
| --- | --- | --- |
| `MessageType.NONE` | `0` | 保留值。 |
| `MessageType.USER` | `1` | 用户发给 bot 的消息。 |
| `MessageType.BOT` | `2` | bot 发给用户的消息。 |
| `MessageState.NEW` | `0` | 新消息。 |
| `MessageState.GENERATING` | `1` | 生成中。 |
| `MessageState.FINISH` | `2` | 已完成。 |

完整示例：

```json
{
  "seq": 129,
  "message_id": 987654322,
  "from_user_id": "wxid_7h3d8k2p9q@im.wechat",
  "to_user_id": "b0f5860fdecb@im.bot",
  "client_id": "wx-msg-1761134400123-a13f9b72",
  "create_time_ms": 1761134400123,
  "update_time_ms": 1761134400123,
  "session_id": "sess_1761134400_d37f2c1e",
  "message_type": 1,
  "message_state": 2,
  "context_token": "Y3R4LXd4aWRfN2gzZDhrMnA5cS0xNzYxMTM0NDAwMDAwLTJhNDcwYTk5",
  "item_list": [
    {
      "type": 1,
      "text_item": {
        "text": "帮我总结一下这张图"
      }
    }
  ]
}
```

### 5.2 `MessageItem`

`item_list` 中的每一项都长这样：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `type` | number | 内容项类型。 |
| `create_time_ms` | number | 项级创建时间。 |
| `update_time_ms` | number | 项级更新时间。 |
| `is_completed` | boolean | 项级完成状态。 |
| `msg_id` | string | 项级 ID。 |
| `ref_msg` | `RefMessage` | 引用消息信息。 |
| `text_item` | object | 文本项内容。 |
| `image_item` | object | 图片项内容。 |
| `voice_item` | object | 语音项内容。 |
| `file_item` | object | 文件项内容。 |
| `video_item` | object | 视频项内容。 |

类型枚举如下：

| `type` | 名称 | 说明 |
| --- | --- | --- |
| `1` | `TEXT` | 文本消息。 |
| `2` | `IMAGE` | 图片消息。 |
| `3` | `VOICE` | 语音消息。 |
| `4` | `FILE` | 文件消息。 |
| `5` | `VIDEO` | 视频消息。 |

### 5.3 5 种 `MessageItem` 结构

#### 5.3.1 `TEXT`

```json
{
  "type": 1,
  "text_item": {
    "text": "请把这张图片里的文字提取出来"
  }
}
```

`TextItem` 只有一个字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `text` | string | 文本内容。 |

#### 5.3.2 `IMAGE`

```json
{
  "type": 2,
  "image_item": {
    "media": {
      "encrypt_query_param": "eyJmaWxlaWQiOiJjZG5fMTc2MTEzNDQwMDAwMF8xIiwic2lnIjoiZG93bmxvYWQifQ==",
      "aes_key": "ABEiM0RVZneImaq7zN3u/w==",
      "encrypt_type": 1
    },
    "aeskey": "00112233445566778899aabbccddeeff",
    "mid_size": 24576,
    "thumb_size": 4096,
    "thumb_height": 320,
    "thumb_width": 320,
    "hd_size": 24576
  }
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `media` | `CDNMedia` | 原图 CDN 引用。 |
| `thumb_media` | `CDNMedia` | 缩略图 CDN 引用。 |
| `aeskey` | string | 原始 16 字节 AES key 的 hex 字符串；图片入站解密时优先使用它。 |
| `url` | string | 可选 URL。当前实现不依赖它做下载。 |
| `mid_size` | number | 中图/原图密文字节数。 |
| `thumb_size` | number | 缩略图密文字节数。 |
| `thumb_height` | number | 缩略图高。 |
| `thumb_width` | number | 缩略图宽。 |
| `hd_size` | number | 高清图密文字节数。 |

#### 5.3.3 `VOICE`

```json
{
  "type": 3,
  "voice_item": {
    "media": {
      "encrypt_query_param": "eyJmaWxlaWQiOiJjZG5fMTc2MTEzNDQwMDAwMF8yIiwic2lnIjoiZG93bmxvYWQifQ==",
      "aes_key": "MDAxMTIyMzM0NDU1NjY3Nzg4OTlhYWJiY2NkZGVlZmY=",
      "encrypt_type": 1
    },
    "encode_type": 6,
    "bits_per_sample": 16,
    "sample_rate": 24000,
    "playtime": 3120,
    "text": "会议几点开始"
  }
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `media` | `CDNMedia` | 语音 CDN 引用。 |
| `encode_type` | number | 编码类型；已观测到的枚举为 `1=pcm`、`2=adpcm`、`3=feature`、`4=speex`、`5=amr`、`6=silk`、`7=mp3`、`8=ogg-speex`。 |
| `bits_per_sample` | number | 采样位深。 |
| `sample_rate` | number | 采样率，单位 Hz。 |
| `playtime` | number | 语音时长，单位毫秒。 |
| `text` | string | 语音转写结果。若存在，当前实现会直接把它作为入站文本正文。 |

#### 5.3.4 `FILE`

```json
{
  "type": 4,
  "file_item": {
    "media": {
      "encrypt_query_param": "eyJmaWxlaWQiOiJjZG5fMTc2MTEzNDQwMDAwMF8zIiwic2lnIjoiZG93bmxvYWQifQ==",
      "aes_key": "MDAxMTIyMzM0NDU1NjY3Nzg4OTlhYWJiY2NkZGVlZmY=",
      "encrypt_type": 1
    },
    "file_name": "budget-2026.xlsx",
    "md5": "6f5902ac237024bdd0c176cb93063dc4",
    "len": "83412"
  }
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `media` | `CDNMedia` | 文件 CDN 引用。 |
| `file_name` | string | 文件名。 |
| `md5` | string | 明文 MD5。 |
| `len` | string | 明文字节数，注意类型是字符串。 |

#### 5.3.5 `VIDEO`

```json
{
  "type": 5,
  "video_item": {
    "media": {
      "encrypt_query_param": "eyJmaWxlaWQiOiJjZG5fMTc2MTEzNDQwMDAwMF80Iiwic2lnIjoiZG93bmxvYWQifQ==",
      "aes_key": "MDAxMTIyMzM0NDU1NjY3Nzg4OTlhYWJiY2NkZGVlZmY=",
      "encrypt_type": 1
    },
    "video_size": 5242880,
    "play_length": 19000,
    "video_md5": "8f14e45fceea167a5a36dedd4bea2543",
    "thumb_media": {
      "encrypt_query_param": "eyJmaWxlaWQiOiJjZG5fMTc2MTEzNDQwMDAwMF80X3RodW1iIiwic2lnIjoiZG93bmxvYWQifQ==",
      "aes_key": "ABEiM0RVZneImaq7zN3u/w==",
      "encrypt_type": 1
    },
    "thumb_size": 4096,
    "thumb_height": 320,
    "thumb_width": 320
  }
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `media` | `CDNMedia` | 视频 CDN 引用。 |
| `video_size` | number | 视频密文字节数。 |
| `play_length` | number | 播放时长，单位毫秒。 |
| `video_md5` | string | 视频明文 MD5。 |
| `thumb_media` | `CDNMedia` | 视频缩略图 CDN 引用。 |
| `thumb_size` | number | 缩略图密文字节数。 |
| `thumb_height` | number | 缩略图高。 |
| `thumb_width` | number | 缩略图宽。 |

### 5.4 `CDNMedia`

`CDNMedia` 是所有媒体项共用的 CDN 引用结构：

```json
{
  "encrypt_query_param": "eyJmaWxlaWQiOiJjZG5fMTc2MTEzNDQwMDAwMF8xIiwic2lnIjoiZG93bmxvYWQifQ==",
  "aes_key": "ABEiM0RVZneImaq7zN3u/w==",
  "encrypt_type": 1
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `encrypt_query_param` | string | CDN 下载参数，拼到 `/download?encrypted_query_param=...`。 |
| `aes_key` | string | base64 编码的 key 数据，具体编码格式见第 8.4 节。 |
| `encrypt_type` | number | `0=只加密 fileid`，`1=同时打包缩略图/中图等信息`。当前发送实现统一写 `1`。 |

### 5.5 `RefMessage`

`MessageItem` 可以带 `ref_msg`，表示对另一条消息的引用：

```json
{
  "title": "上条消息",
  "message_item": {
    "type": 1,
    "text_item": {
      "text": "请看下这个报表"
    }
  }
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `title` | string | 引用摘要。 |
| `message_item` | `MessageItem` | 被引用的消息项。 |

### 5.6 `context_token`

`context_token` 是整个协议最重要的会话关联字段之一：

- 它由 `getupdates` 的入站消息提供。
- 发送回复时必须在 `sendmessage.msg.context_token` 中原样回传。
- Typing 票据 `getconfig` 也会用到它。
- 当前上游实现把它按 `accountId + from_user_id` 维度做进程内缓存，不会持久化到磁盘。

如果没有 `context_token`，当前发送实现会直接拒绝发消息，因为消息无法正确挂回原对话。

## 6. 消息发送 `sendmessage`

### 6.1 接口定义

- 方法：`POST`
- 路径：`/ilink/bot/sendmessage`
- 作用：下发一条 bot 消息

请求外层结构固定为：

```json
{
  "msg": {
    "from_user_id": "",
    "to_user_id": "wxid_7h3d8k2p9q@im.wechat",
    "client_id": "openclaw-weixin:1761134405123-2a470a99",
    "message_type": 2,
    "message_state": 2,
    "context_token": "Y3R4LXd4aWRfN2gzZDhrMnA5cS0xNzYxMTM0NDAwMDAwLTJhNDcwYTk5",
    "item_list": [
      {
        "type": 1,
        "text_item": {
          "text": "你好，我已经收到图片了。"
        }
      }
    ]
  },
  "base_info": {
    "channel_version": "1.0.0"
  }
}
```

文本消息 `curl` 示例：

```bash
curl 'https://ilinkai.weixin.qq.com/ilink/bot/sendmessage' \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'AuthorizationType: ilink_bot_token' \
  -H 'Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.ilink_bot_demo.2wYH0XxJqO9iN0E4x1Q7FA' \
  -H 'X-WECHAT-UIN: MzA1NDE5ODk2' \
  --data-raw '{
    "msg": {
      "from_user_id": "",
      "to_user_id": "wxid_7h3d8k2p9q@im.wechat",
      "client_id": "openclaw-weixin:1761134405123-2a470a99",
      "message_type": 2,
      "message_state": 2,
      "context_token": "Y3R4LXd4aWRfN2gzZDhrMnA5cS0xNzYxMTM0NDAwMDAwLTJhNDcwYTk5",
      "item_list": [
        {
          "type": 1,
          "text_item": {
            "text": "你好，我已经收到图片了。"
          }
        }
      ]
    },
    "base_info": {
      "channel_version": "1.0.0"
    }
  }'
```

当前实现对成功响应体没有结构化依赖，HTTP `200 OK` 即视为发送成功；`SendMessageResp` 类型本身也是空结构。

### 6.2 字段规则

| 字段 | 规则 |
| --- | --- |
| `from_user_id` | 当前发送实现固定写空字符串 `""`。 |
| `to_user_id` | 必须是目标微信用户 ID。 |
| `client_id` | 需要客户端唯一。当前实现格式为 `openclaw-weixin:<timestamp>-<8hex>`。 |
| `message_type` | bot 发送固定写 `2`。 |
| `message_state` | 当前发送实现固定写 `2`，即 `FINISH`。 |
| `context_token` | 必填，必须原样回传入站消息里的值。 |
| `item_list` | 消息内容数组。当前实现通常一条请求只发一个 item。 |

### 6.3 媒体消息示例

在媒体文件已经上传到 CDN 之后，`sendmessage` 只负责引用媒体：

```bash
curl 'https://ilinkai.weixin.qq.com/ilink/bot/sendmessage' \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'AuthorizationType: ilink_bot_token' \
  -H 'Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.ilink_bot_demo.2wYH0XxJqO9iN0E4x1Q7FA' \
  -H 'X-WECHAT-UIN: MzA1NDE5ODk2' \
  --data-raw '{
    "msg": {
      "from_user_id": "",
      "to_user_id": "wxid_7h3d8k2p9q@im.wechat",
      "client_id": "openclaw-weixin:1761134406123-8b7c9d01",
      "message_type": 2,
      "message_state": 2,
      "context_token": "Y3R4LXd4aWRfN2gzZDhrMnA5cS0xNzYxMTM0NDAwMDAwLTJhNDcwYTk5",
      "item_list": [
        {
          "type": 2,
          "image_item": {
            "media": {
              "encrypt_query_param": "eyJmaWxlaWQiOiJjZG5fMTc2MTEzNDQwMDAwMF8xIiwic2lnIjoiZG93bmxvYWQifQ==",
              "aes_key": "ABEiM0RVZneImaq7zN3u/w==",
              "encrypt_type": 1
            },
            "mid_size": 24576
          }
        }
      ]
    },
    "base_info": {
      "channel_version": "1.0.0"
    }
  }'
```

### 6.4 `message_type` / `message_state`

当前已观测到的稳定用法如下：

- 用户入站消息：`message_type = 1`
- bot 出站消息：`message_type = 2`
- 出站消息当前统一使用 `message_state = 2`

虽然类型定义里存在 `message_state = 1 (GENERATING)`，但本次检查到的发送实现没有用它做流式中间态；实际“正在输入”效果是通过 `sendtyping` 提供的，而不是通过多次更新同一条 `sendmessage`。

### 6.5 `context_token`

回复消息时，`context_token` 不是可选优化，而是硬约束：

- 少了它，平台无法把回复关联到对应的上下文。
- 当前实现缺失时会直接抛错：`contextToken is required`。
- 因为 `context_token` 来源于上一次入站消息，所以主动发起新对话不是这个协议栈的主路径；典型模式是“收到消息 -> 带同一个 `context_token` 回复”。

### 6.6 分片策略

协议本身允许 `item_list` 是数组，但当前上游发送策略更保守：

1. 纯文本：一条 `sendmessage`，`item_list` 里只有一个 `TEXT` item。
2. 文本 + 媒体：拆成两条请求，先发文本，再发媒体。
3. 多个媒体：每个媒体各发一次 `sendmessage`，保证每条请求的 `item_list` 只有一个 item。
4. 文本长度分片：上层 channel 配置里把单段文本限制为 4000 字符，超长文本应在进入 `sendmessage` 之前切段。

这意味着如果你要兼容现有实现，最稳妥的做法就是“单 item 发送”，不要依赖一次请求同时携带多个异构 item。

补充说明：协议类型里有 `VOICE` item 和 `UploadMediaType.VOICE = 4`，但本次检查到的出站封装只实现了图片、视频和普通文件上传发送，没有封装语音出站 helper。

## 7. Typing 状态

Typing 由两个接口组合完成：

1. `getconfig` 获取某个用户对应的 `typing_ticket`
2. `sendtyping` 上报开始 / 结束输入状态

### 7.1 `getconfig`

- 方法：`POST`
- 路径：`/ilink/bot/getconfig`

请求体：

```json
{
  "ilink_user_id": "wxid_7h3d8k2p9q@im.wechat",
  "context_token": "Y3R4LXd4aWRfN2gzZDhrMnA5cS0xNzYxMTM0NDAwMDAwLTJhNDcwYTk5",
  "base_info": {
    "channel_version": "1.0.0"
  }
}
```

`curl` 示例：

```bash
curl 'https://ilinkai.weixin.qq.com/ilink/bot/getconfig' \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'AuthorizationType: ilink_bot_token' \
  -H 'Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.ilink_bot_demo.2wYH0XxJqO9iN0E4x1Q7FA' \
  -H 'X-WECHAT-UIN: MzA1NDE5ODk2' \
  --data-raw '{
    "ilink_user_id": "wxid_7h3d8k2p9q@im.wechat",
    "context_token": "Y3R4LXd4aWRfN2gzZDhrMnA5cS0xNzYxMTM0NDAwMDAwLTJhNDcwYTk5",
    "base_info": {
      "channel_version": "1.0.0"
    }
  }'
```

典型响应：

```json
{
  "ret": 0,
  "errmsg": "",
  "typing_ticket": "dHlwLXd4aWRfN2gzZDhrMnA5cS0xNzYxMTM0NDAwMDAwLWY4N2QwYQ=="
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `ret` | number | `0` 表示成功。 |
| `errmsg` | string | 错误信息。 |
| `typing_ticket` | string | base64 编码的 typing ticket。 |

当前上游实现会按 `userId` 缓存 `typing_ticket`，缓存窗口最长 24 小时；刷新失败时按 `2s -> 4s -> 8s ... -> 1h` 做指数退避。

### 7.2 `sendtyping`

- 方法：`POST`
- 路径：`/ilink/bot/sendtyping`

状态枚举：

- `1 = TYPING`
- `2 = CANCEL`

开始输入 `curl` 示例：

```bash
curl 'https://ilinkai.weixin.qq.com/ilink/bot/sendtyping' \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'AuthorizationType: ilink_bot_token' \
  -H 'Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.ilink_bot_demo.2wYH0XxJqO9iN0E4x1Q7FA' \
  -H 'X-WECHAT-UIN: MzA1NDE5ODk2' \
  --data-raw '{
    "ilink_user_id": "wxid_7h3d8k2p9q@im.wechat",
    "typing_ticket": "dHlwLXd4aWRfN2gzZDhrMnA5cS0xNzYxMTM0NDAwMDAwLWY4N2QwYQ==",
    "status": 1,
    "base_info": {
      "channel_version": "1.0.0"
    }
  }'
```

取消输入 `curl` 示例：

```bash
curl 'https://ilinkai.weixin.qq.com/ilink/bot/sendtyping' \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'AuthorizationType: ilink_bot_token' \
  -H 'Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.ilink_bot_demo.2wYH0XxJqO9iN0E4x1Q7FA' \
  -H 'X-WECHAT-UIN: MzA1NDE5ODk2' \
  --data-raw '{
    "ilink_user_id": "wxid_7h3d8k2p9q@im.wechat",
    "typing_ticket": "dHlwLXd4aWRfN2gzZDhrMnA5cS0xNzYxMTM0NDAwMDAwLWY4N2QwYQ==",
    "status": 2,
    "base_info": {
      "channel_version": "1.0.0"
    }
  }'
```

典型响应：

```json
{
  "ret": 0,
  "errmsg": ""
}
```

当前上游实现会在模型生成期间每 5 秒发一次 `status=1` keepalive，结束时发一次 `status=2` 取消。

## 8. 媒体处理 CDN

### 8.1 上传总流程

媒体发送的完整流程如下：

1. 读取明文文件。
2. 计算 `rawsize` 和 `rawfilemd5`。
3. 生成随机 `filekey` 和随机 16 字节 `aeskey`。
4. 按 AES-128-ECB + PKCS7 计算密文大小 `filesize`。
5. 调用 `getuploadurl` 获取 `upload_param`。
6. 用同一个 `aeskey` 对文件明文加密后上传到 CDN。
7. 从 CDN 响应头中取回 `x-encrypted-param`，把它填入后续消息里的 `media.encrypt_query_param`。
8. 把 `aeskey` 按协议编码成 `CDNMedia.aes_key`，再通过 `sendmessage` 引用该媒体。

`filesize` 的计算公式与上游实现完全一致：

```text
filesize = ceil((rawsize + 1) / 16) * 16
```

也就是先按 PKCS7 至少补 1 字节，再对齐到 16 字节块边界。

### 8.2 `getuploadurl`

- 方法：`POST`
- 路径：`/ilink/bot/getuploadurl`

`media_type` 枚举如下：

| 数值 | 名称 |
| --- | --- |
| `1` | `IMAGE` |
| `2` | `VIDEO` |
| `3` | `FILE` |
| `4` | `VOICE` |

请求体示例：

```json
{
  "filekey": "4f2a6c9981f74e0bb2214cdb88a1a245",
  "media_type": 1,
  "to_user_id": "wxid_7h3d8k2p9q@im.wechat",
  "rawsize": 24563,
  "rawfilemd5": "6f5902ac237024bdd0c176cb93063dc4",
  "filesize": 24576,
  "no_need_thumb": true,
  "aeskey": "00112233445566778899aabbccddeeff",
  "base_info": {
    "channel_version": "1.0.0"
  }
}
```

`curl` 示例：

```bash
curl 'https://ilinkai.weixin.qq.com/ilink/bot/getuploadurl' \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'AuthorizationType: ilink_bot_token' \
  -H 'Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.ilink_bot_demo.2wYH0XxJqO9iN0E4x1Q7FA' \
  -H 'X-WECHAT-UIN: MzA1NDE5ODk2' \
  --data-raw '{
    "filekey": "4f2a6c9981f74e0bb2214cdb88a1a245",
    "media_type": 1,
    "to_user_id": "wxid_7h3d8k2p9q@im.wechat",
    "rawsize": 24563,
    "rawfilemd5": "6f5902ac237024bdd0c176cb93063dc4",
    "filesize": 24576,
    "no_need_thumb": true,
    "aeskey": "00112233445566778899aabbccddeeff",
    "base_info": {
      "channel_version": "1.0.0"
    }
  }'
```

典型响应：

```json
{
  "upload_param": "eyJmaWxlaWQiOiJ1cF8xNzYxMTM0NDAwMDAwXzEiLCJzaWciOiJ1cGxvYWQifQ==",
  "thumb_upload_param": ""
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `filekey` | string | 客户端生成的 16 字节随机值的 hex 字符串。 |
| `media_type` | number | 见上表。 |
| `to_user_id` | string | 目标用户 ID。 |
| `rawsize` | number | 明文字节数。 |
| `rawfilemd5` | string | 明文 MD5，hex 字符串。 |
| `filesize` | number | AES-ECB 加密后的密文字节数。 |
| `thumb_rawsize` | number | 缩略图明文字节数。 |
| `thumb_rawfilemd5` | string | 缩略图明文 MD5。 |
| `thumb_filesize` | number | 缩略图密文字节数。 |
| `no_need_thumb` | boolean | 是否不需要缩略图上传 URL。 |
| `aeskey` | string | 原始 16 字节 AES key 的 hex 字符串。 |
| `upload_param` | string | 原文件上传参数。 |
| `thumb_upload_param` | string | 缩略图上传参数。 |

当前上游上传实现始终设置 `no_need_thumb: true`，因此不会再发第二次缩略图上传；但从字段设计看，协议本身支持图片和视频的双上传。

### 8.3 CDN 上传

CDN 上传 URL 规则如下：

```text
POST {cdnBaseUrl}/upload?encrypted_query_param={upload_param}&filekey={filekey}
```

默认 `cdnBaseUrl`：

```text
https://novac2c.cdn.weixin.qq.com/c2c
```

请求要求：

- `Content-Type: application/octet-stream`
- body 是文件明文经 AES-128-ECB + PKCS7 加密后的密文

`curl` 示例：

```bash
curl -i \
  'https://novac2c.cdn.weixin.qq.com/c2c/upload?encrypted_query_param=eyJmaWxlaWQiOiJ1cF8xNzYxMTM0NDAwMDAwXzEiLCJzaWciOiJ1cGxvYWQifQ%3D%3D&filekey=4f2a6c9981f74e0bb2214cdb88a1a245' \
  -X POST \
  -H 'Content-Type: application/octet-stream' \
  --data-binary '@/tmp/weixin-demo/report.png.aes.bin'
```

成功时关键响应头：

```http
HTTP/1.1 200 OK
x-encrypted-param: eyJmaWxlaWQiOiJjZG5fMTc2MTEzNDQwMDAwMF8xIiwic2lnIjoiZG93bmxvYWQifQ==
```

处理规则：

- HTTP `200` 且带 `x-encrypted-param`：上传成功。
- HTTP `4xx`：视为客户端请求错误，不重试。
- HTTP `5xx` 或其他非 `200`：视为服务端错误，当前实现最多重试 3 次。
- `200` 但缺少 `x-encrypted-param`：视为失败。

上传成功后，把响应头 `x-encrypted-param` 保存到消息里的 `media.encrypt_query_param`。

### 8.4 CDN 下载与解密

CDN 下载 URL 规则如下：

```text
GET {cdnBaseUrl}/download?encrypted_query_param={encrypt_query_param}
```

`curl` 示例：

```bash
curl \
  'https://novac2c.cdn.weixin.qq.com/c2c/download?encrypted_query_param=eyJmaWxlaWQiOiJjZG5fMTc2MTEzNDQwMDAwMF8xIiwic2lnIjoiZG93bmxvYWQifQ%3D%3D' \
  --output '/tmp/weixin-demo/report.png.aes.bin'
```

下载到的是密文，需要使用对应的 AES key 解密。当前实现的解密规则如下：

- 算法：`AES-128-ECB`
- Padding：`PKCS7`
- 明文输出：解密后得到原始文件内容

### 8.5 `aes_key` 编码格式

这是媒体协议里最容易踩坑的点。当前实现明确处理了两种编码方式：

| 媒体类型 | JSON 字段示例 | 解释 |
| --- | --- | --- |
| 图片 | `"aes_key": "ABEiM0RVZneImaq7zN3u/w=="` | base64 解码后直接得到 16 字节原始 key。 |
| 文件 / 语音 / 视频 | `"aes_key": "MDAxMTIyMzM0NDU1NjY3Nzg4OTlhYWJiY2NkZGVlZmY="` | base64 解码后得到 ASCII 文本 `00112233445566778899aabbccddeeff`，再按 hex 解析成 16 字节 key。 |

另外还有一个图片特有字段：

```json
{
  "aeskey": "00112233445566778899aabbccddeeff"
}
```

当 `image_item.aeskey` 存在时，当前入站下载实现优先使用它，再退回 `image_item.media.aes_key`。

### 8.6 明文下载的例外情况

对于图片，如果消息里没有 `aeskey` 也没有 `media.aes_key`，当前实现会尝试把 CDN 响应当作明文直接保存，而不是强制解密失败。这说明某些图片流量可能是“有 `encrypt_query_param` 但无额外 AES key”的特殊形态。

## 9. Session 管理

### 9.1 `errcode = -14` 的含义

当前已明确识别出的 session 级错误码只有一个：

- `errcode = -14`
- 或者某些情况下 `ret = -14`

它表示 bot session 已过期，现有 `bot_token` 不再可用。

### 9.2 当前实现的保护策略

上游实现对 `-14` 的处理不是“立刻疯狂重试”，而是带保护的暂停策略：

1. `getupdates` 检测到 `errcode = -14` 或 `ret = -14`。
2. 把该账号标记为 paused，暂停时长固定 1 小时。
3. 监控循环在这 1 小时内不再继续高频轮询。
4. 出站发送前也会检查 paused 状态；如果还在暂停窗口内，直接拒绝发送。

这套策略的目的不是修复 session，而是避免 token 已失效时进入热循环。

### 9.3 推荐重连策略

从协议角度，`-14` 没有自动 refresh token 接口，因此推荐这样处理：

1. 停止使用当前 `bot_token` 继续请求。
2. 重新走第 2 节的二维码登录流程。
3. 保存新的 `bot_token`、`baseurl`、`ilink_bot_id`、`ilink_user_id`。
4. 重新启动 `getupdates` 长轮询。
5. 等待新的入站消息重新建立 `context_token` 缓存。

关于 `get_updates_buf`，推荐做法如下：

- 如果只是 token 过期，但你的消费状态仍可信，可以继续使用旧游标。
- 如果重登后服务端上下文发生明显切换，或者你发现旧游标不再可用，就把 `get_updates_buf` 重置为空字符串重新开始。

## 10. 完整消息流程图（ASCII）

```text
┌────────────────────┐
│ 1. QR 登录         │
└────────┬───────────┘
         │ GET /ilink/bot/get_bot_qrcode?bot_type=3
         v
┌────────────────────┐
│ qrcode + image URL │
└────────┬───────────┘
         │ GET /ilink/bot/get_qrcode_status?qrcode=...
         │ Header: iLink-App-ClientVersion: 1
         v
┌─────────────────────────────────────────────────────┐
│ wait / scaned / expired / confirmed                │
│ confirmed -> bot_token + ilink_bot_id + user_id    │
│            + baseurl                               │
└────────┬────────────────────────────────────────────┘
         │
         │ POST /ilink/bot/getupdates
         │ Authorization: Bearer bot_token
         │ body: { get_updates_buf, base_info }
         v
┌─────────────────────────────────────────────────────┐
│ 2. 入站消息                                          │
│ msgs[] + context_token + next get_updates_buf      │
└────────┬────────────────────────────────────────────┘
         │
         │ 可选: POST /ilink/bot/getconfig
         │      body: { ilink_user_id, context_token, base_info }
         v
┌─────────────────────────────────────────────────────┐
│ typing_ticket                                      │
└────────┬────────────────────────────────────────────┘
         │
         │ 生成回复期间每 5s 可发送一次
         │ POST /ilink/bot/sendtyping status=1
         v
┌─────────────────────────────────────────────────────┐
│ 3. 发送回复                                          │
└────────┬────────────────────────────────────────────┘
         │
         ├─ 文本回复
         │   POST /ilink/bot/sendmessage
         │   body.msg.context_token = 入站 context_token
         │
         └─ 媒体回复
             POST /ilink/bot/getuploadurl
             -> POST {cdn}/upload (AES-128-ECB 密文)
             -> x-encrypted-param
             -> POST /ilink/bot/sendmessage 引用 CDNMedia
         │
         │ 完成后
         │ POST /ilink/bot/sendtyping status=2
         v
┌─────────────────────────────────────────────────────┐
│ 4. 下一轮 getupdates                               │
└─────────────────────────────────────────────────────┘

异常分支：

getupdates 返回 errcode=-14 / ret=-14
    -> session 过期
    -> 暂停请求
    -> 重新走 QR 登录
```

## 11. 错误码参考表

下表只列出本次从源代码中可以明确确认的返回值与错误状态，不对未观测到的私有错误码做猜测。

| 作用域 | 代码 / 状态 | 含义 | 客户端处理建议 |
| --- | --- | --- | --- |
| 所有 CGI JSON 接口 | `ret = 0` 且 `errcode` 缺失或为 `0` | 成功 | 正常处理响应。 |
| `getupdates` | `errcode = -14` | session 过期 | 停止使用当前 token，重新扫码登录。 |
| `getupdates` | `ret = -14` | session 过期的另一种返回形态 | 同上。 |
| 所有 CGI JSON 接口 | `ret != 0` 或 `errcode != 0` 且不为 `-14` | 通用业务失败 | 读取 `errmsg`，短暂重试，连续失败时退避。 |
| `get_qrcode_status` | `status = expired` | 二维码过期 | 重新请求 `get_bot_qrcode`。 |
| CDN 上传 | HTTP `4xx` | 请求参数或上传体错误 | 不要盲目重试，先修复请求。 |
| CDN 上传 | HTTP `5xx` 或非 `200` | CDN 侧瞬时故障 | 可以有限重试；当前实现最多 3 次。 |
| CDN 上传 | `200` 但缺少 `x-encrypted-param` | 上传不完整 | 视为失败，不要继续 `sendmessage`。 |
| 本地长轮询 | 客户端 `AbortError` | 本地超时，不代表服务端出错 | 视为一次空轮询，带原游标继续请求。 |

补充说明：

- 当前源代码里唯一被显式命名并纳入 session 管理逻辑的业务错误码就是 `-14`。
- `errmsg` 只在失败时作为辅助诊断字段使用，不能替代 `ret` / `errcode` 判断成功与否。
