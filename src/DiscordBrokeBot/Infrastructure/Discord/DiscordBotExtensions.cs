using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBrokeBot.Auth;
using DiscordBrokeBot.Features.Orders;
using DiscordBrokeBot.Features.Orders.Models;
using Microsoft.Extensions.Options;

namespace DiscordBrokeBot.Infrastructure.Discord;

public static class DiscordBotExtensions
{
    public static IServiceCollection AddDiscordBot(this IServiceCollection services)
    {
        services.AddSingleton(_ => new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
            LogLevel = LogSeverity.Warning,
            AlwaysDownloadUsers = false,
        }));
        services.AddSingleton(provider => new InteractionService(
            provider.GetRequiredService<DiscordSocketClient>().Rest,
            new InteractionServiceConfig
            {
                UseCompiledLambda = true,
            }));
        services.AddSingleton<DiscordRateLimiter>();
        services.AddHostedService<DiscordBotHostedService>();
        return services;
    }
}

public sealed class DiscordBotHostedService(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services,
    IOptions<DiscordOptions> options,
    ILogger<DiscordBotHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.BotToken))
        {
            logger.LogWarning("Discord:BotToken is empty; Discord Gateway is skipped.");
            return;
        }

        client.Log += OnLogAsync;
        client.InteractionCreated += HandleInteractionAsync;
        client.Ready += RegisterGlobalCommandsAsync;
        await interactions.AddModulesAsync(typeof(OrderInteractionModule).Assembly, services);
        await client.LoginAsync(TokenType.Bot, options.Value.BotToken);
        await client.StartAsync();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        finally
        {
            await client.StopAsync();
            await client.LogoutAsync();
        }
    }

    private async Task RegisterGlobalCommandsAsync()
    {
        try
        {
            await interactions.RegisterCommandsGloballyAsync();
            logger.LogInformation("Discord global application commands registered.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Discord global application command registration failed.");
        }
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        var context = new SocketInteractionContext(client, interaction);
        try
        {
            var result = await interactions.ExecuteCommandAsync(context, services);
            if (!result.IsSuccess)
                logger.LogWarning("Discord interaction failed: {Error}", result.ErrorReason);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Discord interaction execution failed.");
            if (!interaction.HasResponded)
                await interaction.RespondAsync(
                    embed: new EmbedBuilder()
                        .WithTitle("操作失敗")
                        .WithDescription("操作失敗，請稍後再試。")
                        .Build(),
                    ephemeral: true);
        }
    }

    private Task OnLogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            _ => LogLevel.Debug,
        };
        logger.Log(level, message.Exception, "Discord: {Message}", message.Message);
        return Task.CompletedTask;
    }
}

[Group("order", "管理代購訂單")]
public sealed class OrderInteractionModule(
    OrderService orderService,
    DiscordRateLimiter rateLimiter,
    QuickLoginService quickLoginService,
    ILogger<OrderInteractionModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("add", "建立一筆代購訂單")]
    [RequireContext(ContextType.Guild)]
    public async Task AddAsync(
        [Summary("buyer", "負責購買此訂單的 Discord 使用者")]
        IUser buyer,
        [Summary("item", "需要代購的物品名稱")]
        string item,
        [Summary("unit-price", "大於零的新台幣單價")]
        long unitPrice,
        [Summary("quantity", "需要購買的數量")]
        int quantity,
        [Summary("note", "訂單備註")]
        string? note = null,
        [Summary("stall", "販售攤位名稱或編號")]
        string? stall = null)
    {
        if (!await AllowAsync("add"))
            return;
        await DeferAsync(ephemeral: true);
        if (buyer.IsBot)
        {
            await FollowupAsync(
                embed: BuildEmbed("無法建立訂單", "不可指定 Bot 帳號為代購方。"),
                ephemeral: true);
            return;
        }

        var guild = Context.Guild;
        if (guild is null)
        {
            await FollowupAsync(
                embed: BuildEmbed("無法建立訂單", "此指令只能在 Discord 伺服器中使用。"),
                ephemeral: true);
            return;
        }

        var command = new CreateOrderCommand(
            Context.User.Id.ToString(),
            Context.User.Username,
            buyer.Id.ToString(),
            buyer.Username,
            guild.Id.ToString(),
            guild.Name,
            item,
            unitPrice,
            quantity,
            note ?? "",
            stall);
        var order = await orderService.CreateAsync(command, CancellationToken.None);

        var dmWarning = "";
        if (buyer.Id != Context.User.Id)
        {
            try
            {
                EmbedBuilder embedBuilder = new EmbedBuilder()
                    .WithTitle("訂單建立通知")
                    .WithDescription($"{Context.User.Username} ({Context.User.Id}) 已指定你為代購方")
                    .AddField("訂單編號", $"#{order.Id}")
                    .AddField("商品名稱", order.ItemName)
                    .AddField("單價", $"NT$ {order.UnitPrice}")
                    .AddField("數量", order.Quantity.ToString())
                    .AddField("總額", $"NT$ {order.OrderTotal}");

                if (!string.IsNullOrEmpty(order.Stall))
                    embedBuilder.AddField("攤位名稱或編號", order.Stall);

                if (!string.IsNullOrEmpty(order.Note))
                    embedBuilder.AddField("備註", order.Note);

                await buyer.SendMessageAsync(embed: embedBuilder.Build());
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not send order creation DM to {BuyerId}.", buyer.Id);
                dmWarning = "\n但無法傳送私訊通知代購方。";
            }
        }

        var responseEmbedBuilder = new EmbedBuilder()
            .WithTitle("訂單建立成功")
            .AddField("訂單編號", $"#{order.Id}")
            .AddField("商品名稱", order.ItemName)
            .AddField("單價", $"NT$ {order.UnitPrice}")
            .AddField("數量", order.Quantity.ToString())
            .AddField("總額", $"NT$ {order.OrderTotal}");

        if (!string.IsNullOrEmpty(order.Stall))
            responseEmbedBuilder.AddField("攤位名稱或編號", order.Stall);

        if (!string.IsNullOrEmpty(order.Note))
            responseEmbedBuilder.AddField("備註", order.Note);

        if (!string.IsNullOrWhiteSpace(dmWarning))
            responseEmbedBuilder.WithDescription(dmWarning.Trim());

        await FollowupAsync(embed: responseEmbedBuilder.Build(), ephemeral: true);
    }

    [SlashCommand("link", "取得前端快速登入網址")]
    public async Task LinkAsync()
    {
        if (!await AllowAsync("link"))
            return;

        var url = quickLoginService.CreateUrl(Context.User.Id.ToString(), Context.User.Username);
        await RespondAsync(
            embed: BuildEmbed(
                "前端快速登入",
                $"[點此開啟前端]({url})\n連結 10 分鐘內有效且只能使用一次。"),
            ephemeral: true);
    }

    private static Embed BuildEmbed(string title, string? description = null)
    {
        var embedBuilder = new EmbedBuilder().WithTitle(title);
        if (description is not null)
            embedBuilder.WithDescription(description);
        return embedBuilder.Build();
    }

    private async Task<bool> AllowAsync(string operation)
    {
        if (await rateLimiter.AllowAsync(Context.User.Id.ToString(), operation))
            return true;
        await RespondAsync(
            embed: BuildEmbed("操作太頻繁", "請稍後再試。"),
            ephemeral: true);
        return false;
    }
}