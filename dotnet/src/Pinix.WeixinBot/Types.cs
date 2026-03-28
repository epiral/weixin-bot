using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pinix.WeixinBot;

public enum MessageType
{
    User = 1,
    Bot = 2,
}

public enum MessageState
{
    New = 0,
    Generating = 1,
    Finish = 2,
}

public enum MessageItemType
{
    Text = 1,
    Image = 2,
    Voice = 3,
    File = 4,
    Video = 5,
}

public sealed class BaseInfo
{
    [JsonPropertyName("channel_version")]
    public string ChannelVersion { get; init; } = string.Empty;
}

public sealed class CdnMedia
{
    [JsonPropertyName("encrypt_query_param")]
    public string EncryptQueryParam { get; init; } = string.Empty;

    [JsonPropertyName("aes_key")]
    public string AesKey { get; init; } = string.Empty;

    [JsonPropertyName("encrypt_type")]
    public int? EncryptType { get; init; }
}

public sealed class TextItem
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed class ImageItem
{
    [JsonPropertyName("media")]
    public CdnMedia? Media { get; init; }

    [JsonPropertyName("aeskey")]
    public string? AesKey { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("mid_size")]
    public JsonElement? MidSize { get; init; }

    [JsonPropertyName("thumb_size")]
    public JsonElement? ThumbSize { get; init; }

    [JsonPropertyName("thumb_height")]
    public int? ThumbHeight { get; init; }

    [JsonPropertyName("thumb_width")]
    public int? ThumbWidth { get; init; }

    [JsonPropertyName("hd_size")]
    public JsonElement? HdSize { get; init; }
}

public sealed class VoiceItem
{
    [JsonPropertyName("media")]
    public CdnMedia? Media { get; init; }

    [JsonPropertyName("encode_type")]
    public int? EncodeType { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("playtime")]
    public int? PlayTime { get; init; }
}

public sealed class FileItem
{
    [JsonPropertyName("media")]
    public CdnMedia? Media { get; init; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; init; }

    [JsonPropertyName("md5")]
    public string? Md5 { get; init; }

    [JsonPropertyName("len")]
    public string? Length { get; init; }
}

public sealed class VideoItem
{
    [JsonPropertyName("media")]
    public CdnMedia? Media { get; init; }

    [JsonPropertyName("video_size")]
    public JsonElement? VideoSize { get; init; }

    [JsonPropertyName("play_length")]
    public int? PlayLength { get; init; }

    [JsonPropertyName("thumb_media")]
    public CdnMedia? ThumbMedia { get; init; }
}

public sealed class RefMessage
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("message_item")]
    public MessageItem? MessageItem { get; init; }
}

public sealed class MessageItem
{
    [JsonPropertyName("type")]
    public MessageItemType Type { get; init; }

    [JsonPropertyName("text_item")]
    public TextItem? TextItem { get; init; }

    [JsonPropertyName("image_item")]
    public ImageItem? ImageItem { get; init; }

    [JsonPropertyName("voice_item")]
    public VoiceItem? VoiceItem { get; init; }

    [JsonPropertyName("file_item")]
    public FileItem? FileItem { get; init; }

    [JsonPropertyName("video_item")]
    public VideoItem? VideoItem { get; init; }

    [JsonPropertyName("ref_msg")]
    public RefMessage? RefMessage { get; init; }
}

public sealed class WeixinMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; init; }

    [JsonPropertyName("from_user_id")]
    public string FromUserId { get; init; } = string.Empty;

    [JsonPropertyName("to_user_id")]
    public string ToUserId { get; init; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("create_time_ms")]
    public long CreateTimeMilliseconds { get; init; }

    [JsonPropertyName("message_type")]
    public MessageType MessageType { get; init; }

    [JsonPropertyName("message_state")]
    public MessageState MessageState { get; init; }

    [JsonPropertyName("context_token")]
    public string ContextToken { get; init; } = string.Empty;

    [JsonPropertyName("item_list")]
    public List<MessageItem> ItemList { get; init; } = [];
}

public sealed class GetUpdatesResponse
{
    [JsonPropertyName("ret")]
    public int Ret { get; init; }

    [JsonPropertyName("msgs")]
    public List<WeixinMessage> Messages { get; init; } = [];

    [JsonPropertyName("get_updates_buf")]
    public string GetUpdatesBuffer { get; init; } = string.Empty;

    [JsonPropertyName("longpolling_timeout_ms")]
    public int? LongPollingTimeoutMilliseconds { get; init; }

    [JsonPropertyName("errcode")]
    public int? ErrorCode { get; init; }

    [JsonPropertyName("errmsg")]
    public string? ErrorMessage { get; init; }
}

public sealed class GetConfigResponse
{
    [JsonPropertyName("typing_ticket")]
    public string? TypingTicket { get; init; }

    [JsonPropertyName("ret")]
    public int? Ret { get; init; }

    [JsonPropertyName("errcode")]
    public int? ErrorCode { get; init; }

    [JsonPropertyName("errmsg")]
    public string? ErrorMessage { get; init; }
}

public sealed class QrCodeResponse
{
    [JsonPropertyName("qrcode")]
    public string QrCode { get; init; } = string.Empty;

    [JsonPropertyName("qrcode_img_content")]
    public string QrCodeImageContent { get; init; } = string.Empty;
}

public sealed class QrStatusResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("bot_token")]
    public string? BotToken { get; init; }

    [JsonPropertyName("ilink_bot_id")]
    public string? ILinkBotId { get; init; }

    [JsonPropertyName("ilink_user_id")]
    public string? ILinkUserId { get; init; }

    [JsonPropertyName("baseurl")]
    public string? BaseUrl { get; init; }
}

public sealed class IncomingMessage
{
    public required string UserId { get; init; }

    public required string Text { get; init; }

    public required string Type { get; init; }

    public required WeixinMessage Raw { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    internal string ContextToken { get; init; } = string.Empty;
}
