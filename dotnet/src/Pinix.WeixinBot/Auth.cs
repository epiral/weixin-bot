using System.Text.Json;

namespace Pinix.WeixinBot;

public sealed class Credentials
{
    public required string Token { get; init; }

    public required string BaseUrl { get; init; }

    public required string AccountId { get; init; }

    public required string UserId { get; init; }
}

internal static class WeixinBotAuth
{
    private const int QrPollIntervalMilliseconds = 2_000;
    private const int NetworkRetryDelayMilliseconds = 2_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    internal static readonly string DefaultTokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".weixin-bot",
        "credentials.json");

    internal static string ResolveTokenPath(string? tokenPath)
    {
        return string.IsNullOrWhiteSpace(tokenPath) ? DefaultTokenPath : tokenPath;
    }

    internal static async Task<Credentials?> LoadCredentialsAsync(
        string? tokenPath,
        CancellationToken cancellationToken)
    {
        var resolvedPath = ResolveTokenPath(tokenPath);
        if (!File.Exists(resolvedPath))
        {
            return null;
        }

        var payload = await File.ReadAllTextAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(payload);
        return ParseCredentials(document.RootElement, resolvedPath);
    }

    internal static Task ClearCredentialsAsync(string? tokenPath)
    {
        var resolvedPath = ResolveTokenPath(tokenPath);
        if (File.Exists(resolvedPath))
        {
            File.Delete(resolvedPath);
        }

        return Task.CompletedTask;
    }

    internal static async Task<Credentials> LoginAsync(
        string baseUrl,
        string? tokenPath,
        bool force,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (!force)
        {
            var existing = await LoadCredentialsAsync(tokenPath, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }
        }

        while (true)
        {
            QrCodeResponse qr;
            try
            {
                qr = await WeixinBotApi.FetchQrCodeAsync(baseUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsTransientLoginError(exception, cancellationToken))
            {
                log($"Failed to fetch QR code: {exception.Message}. Retrying...");
                await Task.Delay(NetworkRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                continue;
            }

            PrintQrInstructions(qr.QrCodeImageContent);

            string? lastStatus = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                QrStatusResponse status;
                try
                {
                    status = await WeixinBotApi.PollQrStatusAsync(baseUrl, qr.QrCode, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsTransientLoginError(exception, cancellationToken))
                {
                    log($"QR status poll failed: {exception.Message}. Retrying...");
                    await Task.Delay(NetworkRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!string.Equals(status.Status, lastStatus, StringComparison.Ordinal))
                {
                    if (string.Equals(status.Status, "scaned", StringComparison.Ordinal))
                    {
                        log("QR code scanned. Confirm the login inside WeChat.");
                    }
                    else if (string.Equals(status.Status, "confirmed", StringComparison.Ordinal))
                    {
                        log("Login confirmed.");
                    }
                    else if (string.Equals(status.Status, "expired", StringComparison.Ordinal))
                    {
                        log("QR code expired. Requesting a new one...");
                    }

                    lastStatus = status.Status;
                }

                if (string.Equals(status.Status, "confirmed", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(status.BotToken)
                        || string.IsNullOrWhiteSpace(status.ILinkBotId)
                        || string.IsNullOrWhiteSpace(status.ILinkUserId))
                    {
                        throw new InvalidOperationException("QR login confirmed, but the API did not return bot credentials.");
                    }

                    var credentials = new Credentials
                    {
                        Token = status.BotToken,
                        BaseUrl = string.IsNullOrWhiteSpace(status.BaseUrl) ? baseUrl : status.BaseUrl,
                        AccountId = status.ILinkBotId,
                        UserId = status.ILinkUserId,
                    };

                    await SaveCredentialsAsync(credentials, tokenPath, cancellationToken).ConfigureAwait(false);
                    return credentials;
                }

                if (string.Equals(status.Status, "expired", StringComparison.Ordinal))
                {
                    break;
                }

                await Task.Delay(QrPollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task SaveCredentialsAsync(
        Credentials credentials,
        string? tokenPath,
        CancellationToken cancellationToken)
    {
        var resolvedPath = ResolveTokenPath(tokenPath);
        var directory = Path.GetDirectoryName(resolvedPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = JsonSerializer.Serialize(
            new
            {
                token = credentials.Token,
                baseUrl = credentials.BaseUrl,
                accountId = credentials.AccountId,
                userId = credentials.UserId,
            },
            JsonOptions);

        await File.WriteAllTextAsync(resolvedPath, payload + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        TrySetPrivateFileMode(resolvedPath);
    }

    private static Credentials ParseCredentials(JsonElement root, string source)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Invalid credentials format in {source}");
        }

        var token = GetString(root, "token");
        var baseUrl = GetString(root, "base_url") ?? GetString(root, "baseUrl");
        var accountId = GetString(root, "account_id") ?? GetString(root, "accountId");
        var userId = GetString(root, "user_id") ?? GetString(root, "userId");

        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(baseUrl)
            || string.IsNullOrWhiteSpace(accountId)
            || string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException($"Invalid credentials format in {source}");
        }

        return new Credentials
        {
            Token = token,
            BaseUrl = baseUrl,
            AccountId = accountId,
            UserId = userId,
        };
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var propertyValue) && propertyValue.ValueKind == JsonValueKind.String)
        {
            return propertyValue.GetString();
        }

        return null;
    }

    private static void PrintQrInstructions(string url)
    {
        Console.Error.WriteLine("[weixin-bot] 在微信中打开以下链接完成登录:");
        Console.Error.WriteLine(url);
    }

    private static void TrySetPrivateFileMode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // Ignore permission setting errors to keep login flow functional across runtimes.
        }
    }

    private static bool IsTransientLoginError(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception switch
        {
            TimeoutException => true,
            IOException => true,
            HttpRequestException => true,
            ApiException apiException when apiException.StatusCode >= 500 => true,
            OperationCanceledException => true,
            _ => false,
        };
    }
}
