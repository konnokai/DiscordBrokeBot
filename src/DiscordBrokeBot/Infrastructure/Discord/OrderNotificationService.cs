using Discord;
using Discord.WebSocket;
using DiscordBrokeBot.Features.Orders.Models;

namespace DiscordBrokeBot.Infrastructure.Discord;

/// <summary>Sends best-effort Discord DMs for order state changes made in the web UI.</summary>
public sealed class OrderNotificationService(
    DiscordSocketClient client,
    ILogger<OrderNotificationService> logger)
{
    public Task NotifyPurchaseStatusAsync(OrderView order, string actorId) =>
        NotifyCounterpartyAsync(
            order,
            actorId,
            new EmbedBuilder()
                .WithTitle("訂單購買狀態更新")
                .WithDescription($"{ActorName(order, actorId)} 已將訂單 #{order.Id} 標記為{(order.IsPurchased ? "已購買" : "尚未購買")}。")
                .AddField("商品", order.ItemName)
                .Build());

    public Task NotifySettlementStatusAsync(OrderView order, string actorId) =>
        NotifyCounterpartyAsync(
            order,
            actorId,
            new EmbedBuilder()
                .WithTitle("訂單收款狀態更新")
                .WithDescription($"{ActorName(order, actorId)} 已將訂單 #{order.Id} 的收款標記為{(order.IsSettlementComplete ? "已完成" : "未完成")}。")
                .AddField("商品", order.ItemName)
                .AddField("未付金額", $"NT$ {order.Balance}")
                .Build());

    public Task NotifyPaymentChangedAsync(
        OrderView order,
        string actorId,
        string action,
        string detail) =>
        NotifyCounterpartyAsync(
            order,
            actorId,
            new EmbedBuilder()
                .WithTitle($"{action}通知")
                .WithDescription($"{ActorName(order, actorId)} 已操作訂單 #{order.Id} 的款項紀錄。")
                .AddField("商品", order.ItemName)
                .AddField("變更內容", detail)
                .AddField("收款狀態", order.IsSettlementComplete ? "已完成" : "未完成")
                .Build());

    private async Task NotifyCounterpartyAsync(OrderView order, string actorId, Embed embed)
    {
        if (client.LoginState != LoginState.LoggedIn)
            return;

        var targetId = order.BuyerDiscordUserId == actorId
            ? order.RequesterDiscordUserId
            : order.BuyerDiscordUserId;
        if (!ulong.TryParse(targetId, out var userId) || targetId == actorId)
            return;

        try
        {
            var user = await client.GetUserAsync(userId);
            if (user is null)
            {
                logger.LogWarning("Discord user {UserId} was not found for order {OrderId} notification.", targetId, order.Id);
                return;
            }

            await user.SendMessageAsync(embed: embed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not send order {OrderId} notification to Discord user {UserId}.",
                order.Id,
                targetId);
        }
    }

    private static string ActorName(OrderView order, string actorId) =>
        order.RequesterDiscordUserId == actorId ? order.RequesterDisplayName
        : order.BuyerDiscordUserId == actorId ? order.BuyerDisplayName
        : actorId;
}
