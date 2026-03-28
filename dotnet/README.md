# Pinix.WeixinBot

.NET 9 SDK for the WeChat iLink Bot API.

## Layout

```text
dotnet/
├── src/Pinix.WeixinBot/   # SDK library
├── examples/EchoBot/      # minimal example
└── Pinix.WeixinBot.sln
```

## Requirements

- .NET SDK `9.0`
- No third-party runtime dependencies

## Quick Start

Run the included example:

```bash
cd dotnet/examples/EchoBot
dotnet run
```

Use the library from another local project:

```bash
dotnet add <your-project>.csproj reference /path/to/weixin-bot/dotnet/src/Pinix.WeixinBot/Pinix.WeixinBot.csproj
```

```csharp
using Pinix.WeixinBot;

var bot = new WeixinBot();
await bot.LoginAsync();

bot.OnMessage(async message =>
{
    Console.WriteLine($"[{message.Timestamp:HH:mm:ss}] {message.UserId}: {message.Text}");
    await bot.SendTypingAsync(message.UserId);
    await bot.ReplyAsync(message, $"你说了: {message.Text}");
});

await bot.RunAsync();
```

## API

### `new WeixinBot(options?)`

Creates a bot client.

- `BaseUrl`: override the iLink API base URL
- `TokenPath`: override the credential file path
- `OnError`: receive polling or handler errors

### `await bot.LoginAsync(force: false)`

Starts QR login if needed, stores credentials locally, and returns the active session.

### `bot.OnMessage(handler)` / `bot.On("message", handler)`

Registers a sync or async message handler. Inbound messages are normalized into:

```csharp
public sealed class IncomingMessage
{
    public string UserId { get; init; }
    public string Text { get; init; }
    public string Type { get; init; } // text | image | voice | file | video
    public WeixinMessage Raw { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
```

### `await bot.ReplyAsync(message, text)`

Replies with the inbound message's `context_token`. The SDK automatically stops typing in the background after sending the reply.

### `await bot.SendTypingAsync(userId)`

Shows the WeChat typing indicator. This only works after the SDK has seen at least one inbound message from that user.

### `await bot.StopTypingAsync(userId)`

Cancels the typing indicator.

### `await bot.SendAsync(userId, text)`

Sends a proactive text message using the cached `context_token` for that user.

### `await bot.RunAsync()`

Starts the long-poll loop, dispatches inbound messages, retries transient failures, and forces a fresh QR login when the session expires.

### `bot.Stop()`

Stops the long-poll loop gracefully.

## Behavior

1. `LoginAsync()` fetches a QR login URL, waits for WeChat confirmation, and stores the returned credentials at `~/.weixin-bot/credentials.json`.
2. `RunAsync()` performs long polling against `getupdates`.
3. Each inbound user message is converted into `IncomingMessage`.
4. `ReplyAsync()` and `SendAsync()` reuse the internally managed `context_token`.
5. When the API returns `errcode = -14`, the SDK clears saved credentials, requests a fresh QR login, and resumes polling with exponential backoff.

## Protocol

See [../PROTOCOL.md](../PROTOCOL.md) for the wire protocol used by this SDK.
