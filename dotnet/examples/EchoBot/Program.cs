using Pinix.WeixinBot;

var bot = new WeixinBot();
await bot.LoginAsync();

bot.OnMessage(async message =>
{
    Console.WriteLine($"[{message.Timestamp:HH:mm:ss}] {message.UserId}: {message.Text}");
    await bot.SendTypingAsync(message.UserId);
    await bot.ReplyAsync(message, $"你说了: {message.Text}");
});

Console.WriteLine("Bot is running. Press Ctrl+C to stop.");

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    bot.Stop();
};

await bot.RunAsync();
