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
                await interaction.RespondAsync("操作失敗，請稍後再試。", ephemeral: true);
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
    ILogger<OrderInteractionModule> logger) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("add", "建立一筆代購訂單")]
    public async Task AddAsync(
        [Summary("buyer", "負責購買此訂單的 Discord 使用者。")]
        IUser buyer,
        [Summary("item", "需要代購的物品名稱。")]
        string item,
        [Summary("unit-price", "大於零的新台幣單價。")]
        long unitPrice,
        [Summary("quantity", "需要購買的數量。")]
        int quantity,
        [Summary("note", "訂單必要說明。")]
        string note,
        [Summary("stall", "販售攤位名稱或編號。")]
        string? stall = null)
    {
        if (!await AllowAsync("add"))
            return;
        await DeferAsync(ephemeral: true);
        if (buyer.IsBot)
        {
            await FollowupAsync("不可指定 Bot 帳號為代購方。", ephemeral: true);
            return;
        }

        var guild = Context.Guild;
        if (guild is null)
        {
            await FollowupAsync("此指令只能在 Discord 伺服器中使用。", ephemeral: true);
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
            note,
            stall);
        var order = await orderService.CreateAsync(command, CancellationToken.None);

        var dmWarning = "";
        if (buyer.Id != Context.User.Id)
        {
            try
            {
                await buyer.SendMessageAsync(
                    $"你被指定為代購方。訂單 #{order.Id}: {order.ItemName}，數量 {order.Quantity}，總額 NT$ {order.OrderTotal}。\n備註: {order.Note}");
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not send order creation DM to {BuyerId}.", buyer.Id);
                dmWarning = "但無法傳送私訊通知代購方。";
            }
        }

        await FollowupAsync($"訂單 #{order.Id} 已建立。{dmWarning}", ephemeral: true);
    }

    [SlashCommand("list", "查看自己的訂單清單")]
    public async Task ListAsync(
        [Summary("view", "要查看的清單。")]
        [Choice("我要代購", "buying")]
        [Choice("我的委託", "requested")]
        [Choice("已封存", "archived")]
        string view = "buying")
    {
        await DeferAsync(ephemeral: true);
        var archived = view == "archived";
        var role = view == "requested" ? "requester" : "buyer";
        var response = await orderService.ListAsync(
            Context.User.Id.ToString(),
            role,
            archived,
            CancellationToken.None);
        if (response.Orders.Count == 0)
        {
            await FollowupAsync("目前沒有符合條件的訂單。", ephemeral: true);
            return;
        }

        var lines = response.Orders.Take(10).Select(order =>
            $"`#{order.Id}` {order.ItemName} | NT$ {order.OrderTotal} | {(order.IsPurchased ? "已購買" : "待購買")} / {(order.IsSettlementComplete ? "已完成" : "未完成")}");
        var components = new ComponentBuilder();
        foreach (var order in response.Orders.Take(5))
            components.WithButton($"查看 #{order.Id}", $"order:view:{order.Id}", ButtonStyle.Secondary);
        await FollowupAsync(
            $"**{GetViewName(view)}**\n{string.Join("\n", lines)}\n\n總額 NT$ {response.Summary.AllOrderTotal}，差額 NT$ {response.Summary.BalanceTotal}",
            components: components.Build(),
            ephemeral: true);
    }

    [ComponentInteraction("order:view:*")]
    public async Task ViewAsync(string id)
    {
        await DeferAsync(ephemeral: true);
        if (!long.TryParse(id, out var orderId))
        {
            await FollowupAsync("訂單編號不正確。", ephemeral: true);
            return;
        }

        var detail = await orderService.GetDetailAsync(
            Context.User.Id.ToString(),
            orderId,
            CancellationToken.None);
        await FollowupAsync(
            $"訂單 #{detail.Order.Id}\n物品: {detail.Order.ItemName}\n總額: NT$ {detail.Order.OrderTotal}\n已收款: NT$ {detail.Order.ReceivedTotal}\n差額: NT$ {detail.Order.Balance}\n備註: {detail.Order.Note}",
            ephemeral: true);
    }

    [SlashCommand("edit", "編輯訂單的一般欄位")]
    public async Task EditAsync(
        [Summary("order-id", "訂單流水號。")]
        long orderId,
        [Summary("item", "物品名稱。")]
        string item,
        [Summary("unit-price", "大於零的新台幣單價。")]
        long unitPrice,
        [Summary("quantity", "大於零的數量。")]
        int quantity,
        [Summary("note", "訂單備註。")]
        string note,
        [Summary("stall", "販售攤位。")]
        string? stall = null)
    {
        if (!await AllowAsync("mutation"))
            return;
        await DeferAsync(ephemeral: true);
        await orderService.UpdateAsync(
            Context.User.Id.ToString(),
            orderId,
            new UpdateOrderCommand(item, unitPrice, quantity, note, stall),
            CancellationToken.None);
        await FollowupAsync($"訂單 #{orderId} 已更新。", ephemeral: true);
    }

    [SlashCommand("purchase", "切換訂單購買狀態")]
    public async Task PurchaseAsync(
        [Summary("order-id", "訂單流水號。")]
        long orderId,
        [Summary("purchased", "是否已購買。")]
        bool purchased)
    {
        if (!await AllowAsync("mutation"))
            return;
        await DeferAsync(ephemeral: true);
        await orderService.UpdatePurchaseStatusAsync(
            Context.User.Id.ToString(),
            orderId,
            purchased,
            CancellationToken.None);
        await FollowupAsync("購買狀態已更新。", ephemeral: true);
    }

    [SlashCommand("settlement", "設定收款完成模式")]
    public async Task SettlementAsync(
        [Summary("order-id", "訂單流水號。")]
        long orderId,
        [Summary("mode", "自動、強制完成或強制未完成。")]
        [Choice("自動判定", SettlementModes.Auto)]
        [Choice("強制完成", SettlementModes.ForceCompleted)]
        [Choice("強制未完成", SettlementModes.ForcePending)]
        string mode)
    {
        if (!await AllowAsync("mutation"))
            return;
        await DeferAsync(ephemeral: true);
        await orderService.UpdateSettlementModeAsync(
            Context.User.Id.ToString(),
            orderId,
            mode,
            CancellationToken.None);
        await FollowupAsync("收款完成模式已更新。", ephemeral: true);
    }

    [SlashCommand("payment-add", "新增款項紀錄")]
    public async Task AddPaymentAsync(
        [Summary("order-id", "訂單流水號。")]
        long orderId,
        [Summary("amount", "可為正數或負數，但不可為零。")]
        long amount,
        [Summary("reason", "款項事由。")]
        string reason)
    {
        if (!await AllowAsync("mutation"))
            return;
        await DeferAsync(ephemeral: true);
        var result = await orderService.AddPaymentAsync(
            Context.User.Id.ToString(),
            orderId,
            amount,
            reason,
            CancellationToken.None);
        await NotifyPaymentAddedAsync(result);
        await FollowupAsync("款項紀錄已新增。", ephemeral: true);
    }

    [SlashCommand("payment-edit", "編輯款項紀錄")]
    public async Task EditPaymentAsync(
        [Summary("payment-id", "款項流水號。")]
        long paymentId,
        [Summary("amount", "可為正數或負數，但不可為零。")]
        long amount,
        [Summary("reason", "款項事由。")]
        string reason)
    {
        if (!await AllowAsync("mutation"))
            return;
        await DeferAsync(ephemeral: true);
        var result = await orderService.UpdatePaymentAsync(
            Context.User.Id.ToString(),
            paymentId,
            amount,
            reason,
            CancellationToken.None);
        await NotifySettlementAsync(result);
        await FollowupAsync("款項紀錄已更新。", ephemeral: true);
    }

    [SlashCommand("payment-delete", "永久刪除款項紀錄")]
    public async Task DeletePaymentAsync(
        [Summary("payment-id", "款項流水號。")]
        long paymentId)
    {
        if (!await AllowAsync("mutation"))
            return;
        await DeferAsync(ephemeral: true);
        var result = await orderService.DeletePaymentAsync(
            Context.User.Id.ToString(),
            paymentId,
            CancellationToken.None);
        await NotifySettlementAsync(result);
        await FollowupAsync("款項紀錄已永久刪除。", ephemeral: true);
    }

    [SlashCommand("archive", "封存訂單")]
    public async Task ArchiveAsync(
        [Summary("order-id", "訂單流水號。")]
        long orderId)
    {
        if (!await AllowAsync("mutation"))
            return;
        await DeferAsync(ephemeral: true);
        await orderService.ArchiveAsync(Context.User.Id.ToString(), orderId, CancellationToken.None);
        await FollowupAsync("訂單已封存。", ephemeral: true);
    }

    [SlashCommand("restore", "復原訂單")]
    public async Task RestoreAsync(
        [Summary("order-id", "訂單流水號。")]
        long orderId)
    {
        if (!await AllowAsync("mutation"))
            return;
        await DeferAsync(ephemeral: true);
        await orderService.RestoreAsync(Context.User.Id.ToString(), orderId, CancellationToken.None);
        await FollowupAsync("訂單已復原。", ephemeral: true);
    }

    [SlashCommand("block", "封鎖請求方建立未來訂單")]
    public async Task BlockAsync(
        [Summary("requester", "要封鎖的請求方。")]
        IUser requester)
    {
        if (!await AllowAsync("mutation"))
            return;
        await DeferAsync(ephemeral: true);
        await orderService.BlockAsync(
            Context.User.Id.ToString(),
            requester.Id.ToString(),
            requester.Username,
            CancellationToken.None);
        await FollowupAsync("已更新封鎖名單。", ephemeral: true);
    }

    [SlashCommand("unblock", "解除封鎖請求方")]
    public async Task UnblockAsync(
        [Summary("requester", "要解除封鎖的請求方。")]
        IUser requester)
    {
        if (!await AllowAsync("mutation"))
            return;
        await DeferAsync(ephemeral: true);
        await orderService.UnblockAsync(
            Context.User.Id.ToString(),
            requester.Id.ToString(),
            CancellationToken.None);
        await FollowupAsync("已解除封鎖。", ephemeral: true);
    }

    [SlashCommand("blocked", "查看封鎖名單")]
    public async Task BlockedAsync()
    {
        await DeferAsync(ephemeral: true);
        var blocks = await orderService.ListBlocksAsync(Context.User.Id.ToString(), CancellationToken.None);
        await FollowupAsync(
            blocks.Count == 0
                ? "目前沒有封鎖任何請求方。"
                : string.Join("\n", blocks.Select(block => $"{block.RequesterDisplayName} (`{block.RequesterDiscordUserId}`)")),
            ephemeral: true);
    }

    private static string GetViewName(string view) => view switch
    {
        "requested" => "我的委託",
        "archived" => "已封存",
        _ => "我要代購",
    };

    private async Task<bool> AllowAsync(string operation)
    {
        if (await rateLimiter.AllowAsync(Context.User.Id.ToString(), operation))
            return true;
        await RespondAsync("操作太頻繁，請稍後再試。", ephemeral: true);
        return false;
    }

    private async Task NotifySettlementAsync(PaymentMutationResult result)
    {
        if (result.WasSettlementComplete || !result.IsSettlementComplete)
            return;
        if (result.RequesterDiscordUserId == Context.User.Id.ToString())
            return;
        try
        {
            var requester = await Context.Client.GetUserAsync(ulong.Parse(result.RequesterDiscordUserId));
            await requester.SendMessageAsync($"訂單 #{result.OrderId} 的收款狀態已完成。");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not send settlement notification for order {OrderId}.", result.OrderId);
        }
    }

    private async Task NotifyPaymentAddedAsync(PaymentMutationResult result)
    {
        if (result.RequesterDiscordUserId == Context.User.Id.ToString())
            return;
        try
        {
            var requester = await Context.Client.GetUserAsync(ulong.Parse(result.RequesterDiscordUserId));
            var message = result.WasSettlementComplete || !result.IsSettlementComplete
                ? $"訂單 #{result.OrderId} 新增了一筆款項紀錄。"
                : $"訂單 #{result.OrderId} 新增了一筆款項紀錄，收款狀態已完成。";
            await requester.SendMessageAsync(message);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not send payment notification for order {OrderId}.", result.OrderId);
        }
    }
}
