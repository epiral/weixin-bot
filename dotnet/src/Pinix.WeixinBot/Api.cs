using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pinix.WeixinBot;

public sealed class ApiException : Exception
{
    public ApiException(string message, int statusCode, int? errorCode = null, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }

    public int? ErrorCode { get; }

    public string? ResponseBody { get; }

    public bool IsSessionExpired => ErrorCode == -14;
}

public static class WeixinBotDefaults
{
    public const string BaseUrl = "https://ilinkai.weixin.qq.com";
    public const string ChannelVersion = "1.0.0";
}

internal static class WeixinBotApi
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string RandomWeChatUin()
    {
        Span<byte> buffer = stackalloc byte[4];
        RandomNumberGenerator.Fill(buffer);
        var value = BinaryPrimitives.ReadUInt32BigEndian(buffer);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    internal static async Task<GetUpdatesResponse> GetUpdatesAsync(
        string baseUrl,
        string token,
        string cursor,
        CancellationToken cancellationToken)
    {
        var request = new GetUpdatesRequest
        {
            GetUpdatesBuffer = cursor,
            BaseInfo = BuildBaseInfo(),
        };

        return await PostAsync<GetUpdatesResponse>(
            baseUrl,
            "/ilink/bot/getupdates",
            request,
            token,
            40_000,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task SendMessageAsync(
        string baseUrl,
        string token,
        SendMessagePayload message,
        CancellationToken cancellationToken)
    {
        var request = new SendMessageRequest
        {
            Message = message,
            BaseInfo = BuildBaseInfo(),
        };

        await PostAsync<JsonElement>(
            baseUrl,
            "/ilink/bot/sendmessage",
            request,
            token,
            15_000,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<GetConfigResponse> GetConfigAsync(
        string baseUrl,
        string token,
        string userId,
        string contextToken,
        CancellationToken cancellationToken)
    {
        var request = new GetConfigRequest
        {
            ILinkUserId = userId,
            ContextToken = contextToken,
            BaseInfo = BuildBaseInfo(),
        };

        return await PostAsync<GetConfigResponse>(
            baseUrl,
            "/ilink/bot/getconfig",
            request,
            token,
            15_000,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task SendTypingAsync(
        string baseUrl,
        string token,
        string userId,
        string ticket,
        int status,
        CancellationToken cancellationToken)
    {
        var request = new SendTypingRequest
        {
            ILinkUserId = userId,
            TypingTicket = ticket,
            Status = status,
            BaseInfo = BuildBaseInfo(),
        };

        await PostAsync<JsonElement>(
            baseUrl,
            "/ilink/bot/sendtyping",
            request,
            token,
            15_000,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<QrCodeResponse> FetchQrCodeAsync(string baseUrl, CancellationToken cancellationToken)
    {
        return await GetAsync<QrCodeResponse>(
            baseUrl,
            "/ilink/bot/get_bot_qrcode?bot_type=3",
            timeoutMilliseconds: 15_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<QrStatusResponse> PollQrStatusAsync(
        string baseUrl,
        string qrCode,
        CancellationToken cancellationToken)
    {
        return await GetAsync<QrStatusResponse>(
            baseUrl,
            $"/ilink/bot/get_qrcode_status?qrcode={Uri.EscapeDataString(qrCode)}",
            headers: new Dictionary<string, string>
            {
                ["iLink-App-ClientVersion"] = "1",
            },
            timeoutMilliseconds: 15_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static SendMessagePayload BuildTextMessage(string userId, string contextToken, string text)
    {
        return new SendMessagePayload
        {
            FromUserId = string.Empty,
            ToUserId = userId,
            ClientId = Guid.NewGuid().ToString(),
            MessageType = MessageType.Bot,
            MessageState = MessageState.Finish,
            ContextToken = contextToken,
            ItemList =
            [
                new MessageItem
                {
                    Type = MessageItemType.Text,
                    TextItem = new TextItem
                    {
                        Text = text,
                    },
                },
            ],
        };
    }

    private static BaseInfo BuildBaseInfo()
    {
        return new BaseInfo
        {
            ChannelVersion = WeixinBotDefaults.ChannelVersion,
        };
    }

    private static async Task<T> PostAsync<T>(
        string baseUrl,
        string path,
        object body,
        string token,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(baseUrl, path))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json"),
        };

        foreach (var header in BuildHeaders(token))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return await SendAsync<T>(request, path, timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> GetAsync<T>(
        string baseUrl,
        string path,
        IReadOnlyDictionary<string, string>? headers = null,
        int timeoutMilliseconds = 15_000,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(baseUrl, path));

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return await SendAsync<T>(request, path, timeoutMilliseconds, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> SendAsync<T>(
        HttpRequestMessage request,
        string label,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeoutMilliseconds);

        try
        {
            using var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token).ConfigureAwait(false);

            var payloadText = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
            return ParseJsonResponse<T>(payloadText, (int)response.StatusCode, label);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{label} timed out after {timeoutMilliseconds} ms.", exception);
        }
        catch (HttpRequestException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException($"{label} request failed: {exception.Message}", exception);
        }
    }

    private static T ParseJsonResponse<T>(string payloadText, int statusCode, string label)
    {
        var normalizedPayload = string.IsNullOrWhiteSpace(payloadText) ? "{}" : payloadText;
        using var document = JsonDocument.Parse(normalizedPayload);
        var root = document.RootElement;

        if (statusCode is < 200 or >= 300)
        {
            throw new ApiException(
                GetString(root, "errmsg") ?? $"{label} failed with HTTP {statusCode}",
                statusCode,
                GetInt32(root, "errcode"),
                normalizedPayload);
        }

        if (root.TryGetProperty("ret", out var retValue)
            && retValue.ValueKind == JsonValueKind.Number
            && retValue.GetInt32() != 0)
        {
            var errorCode = GetInt32(root, "errcode") ?? retValue.GetInt32();
            throw new ApiException(
                GetString(root, "errmsg") ?? $"{label} failed",
                statusCode,
                errorCode,
                normalizedPayload);
        }

        var result = JsonSerializer.Deserialize<T>(normalizedPayload, JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException($"{label} returned an empty JSON payload.");
        }

        return result;
    }

    private static Uri BuildUri(string baseUrl, string path)
    {
        return new(new Uri($"{baseUrl.TrimEnd('/')}/", UriKind.Absolute), path.TrimStart('/'));
    }

    private static Dictionary<string, string> BuildHeaders(string token)
    {
        return new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["AuthorizationType"] = "ilink_bot_token",
            ["Authorization"] = $"Bearer {token}",
            ["X-WECHAT-UIN"] = RandomWeChatUin(),
        };
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.GetInt32();
        }

        return null;
    }

    private sealed class GetUpdatesRequest
    {
        [JsonPropertyName("get_updates_buf")]
        public string GetUpdatesBuffer { get; init; } = string.Empty;

        [JsonPropertyName("base_info")]
        public BaseInfo BaseInfo { get; init; } = new();
    }

    private sealed class GetConfigRequest
    {
        [JsonPropertyName("ilink_user_id")]
        public string ILinkUserId { get; init; } = string.Empty;

        [JsonPropertyName("context_token")]
        public string ContextToken { get; init; } = string.Empty;

        [JsonPropertyName("base_info")]
        public BaseInfo BaseInfo { get; init; } = new();
    }

    private sealed class SendMessageRequest
    {
        [JsonPropertyName("msg")]
        public SendMessagePayload Message { get; init; } = new();

        [JsonPropertyName("base_info")]
        public BaseInfo BaseInfo { get; init; } = new();
    }

    private sealed class SendTypingRequest
    {
        [JsonPropertyName("ilink_user_id")]
        public string ILinkUserId { get; init; } = string.Empty;

        [JsonPropertyName("typing_ticket")]
        public string TypingTicket { get; init; } = string.Empty;

        [JsonPropertyName("status")]
        public int Status { get; init; }

        [JsonPropertyName("base_info")]
        public BaseInfo BaseInfo { get; init; } = new();
    }

    internal sealed class SendMessagePayload
    {
        [JsonPropertyName("from_user_id")]
        public string FromUserId { get; init; } = string.Empty;

        [JsonPropertyName("to_user_id")]
        public string ToUserId { get; init; } = string.Empty;

        [JsonPropertyName("client_id")]
        public string ClientId { get; init; } = string.Empty;

        [JsonPropertyName("message_type")]
        public MessageType MessageType { get; init; }

        [JsonPropertyName("message_state")]
        public MessageState MessageState { get; init; }

        [JsonPropertyName("context_token")]
        public string ContextToken { get; init; } = string.Empty;

        [JsonPropertyName("item_list")]
        public List<MessageItem> ItemList { get; init; } = [];
    }
}
