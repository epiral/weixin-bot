using System.Collections.Concurrent;

namespace Pinix.WeixinBot;

public sealed class WeixinBotOptions
{
    public string? BaseUrl { get; init; }

    public string? TokenPath { get; init; }

    public Action<Exception>? OnError { get; init; }
}

public sealed class WeixinBot
{
    private readonly object _gate = new();
    private readonly List<Func<IncomingMessage, Task>> _handlers = [];
    private readonly ConcurrentDictionary<string, string> _contextTokens = new();
    private readonly string? _tokenPath;
    private readonly Action<Exception>? _onError;

    private string _baseUrl;
    private Credentials? _credentials;
    private string _cursor = string.Empty;
    private bool _stopped;
    private CancellationTokenSource? _runCancellationSource;
    private CancellationTokenSource? _currentPollCancellationSource;
    private Task? _runTask;

    public WeixinBot(WeixinBotOptions? options = null)
    {
        _baseUrl = options?.BaseUrl ?? WeixinBotDefaults.BaseUrl;
        _tokenPath = options?.TokenPath;
        _onError = options?.OnError;
    }

    public static string DefaultTokenPath => WeixinBotAuth.DefaultTokenPath;

    public WeixinBot OnMessage(Func<IncomingMessage, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(handler);
        return this;
    }

    public WeixinBot OnMessage(Action<IncomingMessage> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnMessage(message =>
        {
            handler(message);
            return Task.CompletedTask;
        });
    }

    public WeixinBot On(string eventName, Func<IncomingMessage, Task> handler)
    {
        if (!string.Equals(eventName, "message", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Unsupported event: {eventName}");
        }

        return OnMessage(handler);
    }

    public WeixinBot On(string eventName, Action<IncomingMessage> handler)
    {
        if (!string.Equals(eventName, "message", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Unsupported event: {eventName}");
        }

        return OnMessage(handler);
    }

    public async Task<Credentials> LoginAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var previousToken = _credentials?.Token;
        var credentials = await WeixinBotAuth.LoginAsync(
            _baseUrl,
            _tokenPath,
            force,
            Log,
            cancellationToken).ConfigureAwait(false);

        _credentials = credentials;
        _baseUrl = credentials.BaseUrl;

        if (!string.IsNullOrWhiteSpace(previousToken)
            && !string.Equals(previousToken, credentials.Token, StringComparison.Ordinal))
        {
            _cursor = string.Empty;
            _contextTokens.Clear();
        }

        Log($"Logged in as {credentials.UserId}");
        return credentials;
    }

    public async Task ReplyAsync(
        IncomingMessage message,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _contextTokens[message.UserId] = message.ContextToken;
        await SendTextAsync(message.UserId, text, message.ContextToken, cancellationToken).ConfigureAwait(false);
        IgnoreFault(StopTypingAsync(message.UserId));
    }

    public async Task SendTypingAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_contextTokens.TryGetValue(userId, out var contextToken))
        {
            throw new InvalidOperationException($"No cached context token for user {userId}. Reply to an incoming message first.");
        }

        var credentials = await EnsureCredentialsAsync(cancellationToken).ConfigureAwait(false);
        var config = await WeixinBotApi.GetConfigAsync(
            _baseUrl,
            credentials.Token,
            userId,
            contextToken,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(config.TypingTicket))
        {
            Log("sendTyping: no typing_ticket returned by getconfig");
            return;
        }

        await WeixinBotApi.SendTypingAsync(
            _baseUrl,
            credentials.Token,
            userId,
            config.TypingTicket,
            status: 1,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task StopTypingAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_contextTokens.TryGetValue(userId, out var contextToken))
        {
            return;
        }

        var credentials = await EnsureCredentialsAsync(cancellationToken).ConfigureAwait(false);
        var config = await WeixinBotApi.GetConfigAsync(
            _baseUrl,
            credentials.Token,
            userId,
            contextToken,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(config.TypingTicket))
        {
            return;
        }

        await WeixinBotApi.SendTypingAsync(
            _baseUrl,
            credentials.Token,
            userId,
            config.TypingTicket,
            status: 2,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync(string userId, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_contextTokens.TryGetValue(userId, out var contextToken))
        {
            throw new InvalidOperationException($"No cached context token for user {userId}. Reply to an incoming message first.");
        }

        await SendTextAsync(userId, text, contextToken, cancellationToken).ConfigureAwait(false);
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_runTask is not null)
            {
                return _runTask;
            }

            _stopped = false;
            _runCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = RunAndCleanupAsync(_runCancellationSource.Token);
            return _runTask;
        }
    }

    public void Stop()
    {
        _stopped = true;

        lock (_gate)
        {
            _currentPollCancellationSource?.Cancel();
            _runCancellationSource?.Cancel();
        }
    }

    private async Task RunAndCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunLoopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _currentPollCancellationSource?.Dispose();
                _currentPollCancellationSource = null;
                _runCancellationSource?.Dispose();
                _runCancellationSource = null;
                _runTask = null;
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureCredentialsAsync(cancellationToken).ConfigureAwait(false);
            Log("Long-poll loop started.");
            var retryDelay = TimeSpan.FromSeconds(1);

            while (!_stopped && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var credentials = await EnsureCredentialsAsync(cancellationToken).ConfigureAwait(false);
                    using var pollCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    SetCurrentPollCancellationSource(pollCancellationSource);

                    var updates = await WeixinBotApi.GetUpdatesAsync(
                        _baseUrl,
                        credentials.Token,
                        _cursor,
                        pollCancellationSource.Token).ConfigureAwait(false);

                    ClearCurrentPollCancellationSource(pollCancellationSource);
                    _cursor = string.IsNullOrWhiteSpace(updates.GetUpdatesBuffer) ? _cursor : updates.GetUpdatesBuffer;
                    retryDelay = TimeSpan.FromSeconds(1);

                    foreach (var rawMessage in updates.Messages)
                    {
                        RememberContext(rawMessage);
                        var incomingMessage = ToIncomingMessage(rawMessage);
                        if (incomingMessage is null)
                        {
                            continue;
                        }

                        await DispatchMessageAsync(incomingMessage).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_stopped || cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    ClearCurrentPollCancellationSource(null);

                    if (IsSessionExpired(error))
                    {
                        Log("Session expired. Waiting for a fresh QR login...");
                        _credentials = null;
                        _cursor = string.Empty;
                        _contextTokens.Clear();

                        try
                        {
                            await WeixinBotAuth.ClearCredentialsAsync(_tokenPath).ConfigureAwait(false);
                            await LoginAsync(force: true, cancellationToken).ConfigureAwait(false);
                            retryDelay = TimeSpan.FromSeconds(1);
                            continue;
                        }
                        catch (Exception loginError)
                        {
                            ReportError(loginError);
                        }
                    }
                    else
                    {
                        ReportError(error);
                    }

                    try
                    {
                        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_stopped || cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var nextDelaySeconds = Math.Min(retryDelay.TotalSeconds * 2, 10);
                    retryDelay = TimeSpan.FromSeconds(nextDelaySeconds);
                }
            }
        }
        finally
        {
            Log("Long-poll loop stopped.");
        }
    }

    private async Task<Credentials> EnsureCredentialsAsync(CancellationToken cancellationToken)
    {
        if (_credentials is not null)
        {
            return _credentials;
        }

        var stored = await WeixinBotAuth.LoadCredentialsAsync(_tokenPath, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            _credentials = stored;
            _baseUrl = stored.BaseUrl;
            return stored;
        }

        return await LoginAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task SendTextAsync(
        string userId,
        string text,
        string contextToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException("Message text cannot be empty.", nameof(text));
        }

        var credentials = await EnsureCredentialsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var chunk in ChunkText(text, 2_000))
        {
            await WeixinBotApi.SendMessageAsync(
                _baseUrl,
                credentials.Token,
                WeixinBotApi.BuildTextMessage(userId, contextToken, chunk),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchMessageAsync(IncomingMessage message)
    {
        if (_handlers.Count == 0)
        {
            return;
        }

        var tasks = _handlers.Select(handler => InvokeHandlerAsync(handler, message)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task InvokeHandlerAsync(Func<IncomingMessage, Task> handler, IncomingMessage message)
    {
        try
        {
            await handler(message).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            ReportError(error);
        }
    }

    private void RememberContext(WeixinMessage message)
    {
        var userId = message.MessageType == MessageType.User ? message.FromUserId : message.ToUserId;
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(message.ContextToken))
        {
            _contextTokens[userId] = message.ContextToken;
        }
    }

    private IncomingMessage? ToIncomingMessage(WeixinMessage message)
    {
        if (message.MessageType != MessageType.User)
        {
            return null;
        }

        return new IncomingMessage
        {
            UserId = message.FromUserId,
            Text = ExtractText(message.ItemList),
            Type = DetectType(message.ItemList),
            Raw = message,
            ContextToken = message.ContextToken,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(message.CreateTimeMilliseconds).ToLocalTime(),
        };
    }

    private static string DetectType(IReadOnlyList<MessageItem> items)
    {
        if (items.Count == 0)
        {
            return "text";
        }

        return items[0].Type switch
        {
            MessageItemType.Image => "image",
            MessageItemType.Voice => "voice",
            MessageItemType.File => "file",
            MessageItemType.Video => "video",
            _ => "text",
        };
    }

    private static string ExtractText(IEnumerable<MessageItem> items)
    {
        var parts = new List<string>();

        foreach (var item in items)
        {
            var text = item.Type switch
            {
                MessageItemType.Text => item.TextItem?.Text ?? string.Empty,
                MessageItemType.Image => item.ImageItem?.Url ?? "[image]",
                MessageItemType.Voice => item.VoiceItem?.Text ?? "[voice]",
                MessageItemType.File => item.FileItem?.FileName ?? "[file]",
                MessageItemType.Video => "[video]",
                _ => string.Empty,
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static IEnumerable<string> ChunkText(string text, int limit)
    {
        for (var index = 0; index < text.Length; index += limit)
        {
            yield return text.Substring(index, Math.Min(limit, text.Length - index));
        }
    }

    private static bool IsSessionExpired(Exception error)
    {
        return error is ApiException apiException && apiException.IsSessionExpired;
    }

    private static void IgnoreFault(Task task)
    {
        _ = task.ContinueWith(
            _ => { },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private void ReportError(Exception error)
    {
        Log(error.Message);

        if (_onError is null)
        {
            return;
        }

        try
        {
            _onError(error);
        }
        catch (Exception callbackError)
        {
            Log($"onError callback failed: {callbackError.Message}");
        }
    }

    private void Log(string message)
    {
        Console.Error.WriteLine($"[weixin-bot] {message}");
    }

    private void SetCurrentPollCancellationSource(CancellationTokenSource source)
    {
        lock (_gate)
        {
            _currentPollCancellationSource?.Dispose();
            _currentPollCancellationSource = source;
        }
    }

    private void ClearCurrentPollCancellationSource(CancellationTokenSource? expectedSource)
    {
        lock (_gate)
        {
            if (expectedSource is not null && !ReferenceEquals(_currentPollCancellationSource, expectedSource))
            {
                return;
            }

            _currentPollCancellationSource?.Dispose();
            _currentPollCancellationSource = null;
        }
    }
}
