using DiscordBrokeBot.Features.Orders.Models;
using DiscordBrokeBot.Infrastructure.Discord;

namespace DiscordBrokeBot.Features.Orders;

/// <summary>Applies shared order, amount, settlement, and UID authorization rules.</summary>
/// <remarks>
/// Both Discord interactions and HTTP endpoints call this service. Stores remain the only SQL
/// boundary, so changing the future persistence implementation does not change either contract.
/// </remarks>
public sealed class OrderService(
    OrderStore orderStore,
    PaymentEntryStore paymentEntryStore,
    OrderActivityStore activityStore,
    UserBlockStore userBlockStore,
    OrderNotificationService notificationService,
    ILogger<OrderService> logger)
{
    public async Task<OrderView> CreateAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        ValidateDiscordId(command.RequesterDiscordUserId, "請求方 UID");
        ValidateDiscordId(command.BuyerDiscordUserId, "代購方 UID");
        ValidateDiscordId(command.SourceGuildId, "來源伺服器 UID");
        OrderRules.ValidateOrderText(command.ItemName, command.Note);
        OrderRules.CalculateTotal(command.UnitPrice, command.Quantity);

        var id = await orderStore.CreateIfNotBlockedAsync(command, cancellationToken);
        if (id is null)
            throw new OrderRuleException("代購方已封鎖此請求方，無法建立訂單。");

        var view = await GetAsync(command.RequesterDiscordUserId, id.Value, cancellationToken);
        await RecordActivityAsync(
            id.Value,
            command.RequesterDiscordUserId,
            command.RequesterDisplayName,
            "建立訂單",
            $"建立訂單：{view.ItemName} × {view.Quantity}，單價 NT$ {view.UnitPrice}，總額 NT$ {view.OrderTotal}。",
            cancellationToken);
        return view;
    }

    public async Task<OrdersResponse> ListAsync(
        string actorId,
        string role,
        bool archived,
        CancellationToken cancellationToken)
    {
        ValidateDiscordId(actorId, "操作者 UID");
        if (role is not ("buyer" or "requester"))
            throw new OrderRuleException("訂單角色不正確。");

        var rows = await orderStore.ListAsync(actorId, role, archived, cancellationToken);
        var views = rows.Select(row => ToView(row, actorId)).ToArray();
        var all = 0L;
        var unpurchased = 0L;
        var purchased = 0L;
        var received = 0L;
        var balance = 0L;

        foreach (var row in rows)
        {
            var financials = OrderRules.CalculateFinancials(
                row.UnitPrice,
                row.Quantity,
                row.ReceivedTotal,
                row.SettlementOverride);
            all = OrderRules.AddAmounts(all, financials.OrderTotal);
            received = OrderRules.AddAmounts(received, financials.ReceivedTotal);
            balance = OrderRules.AddAmounts(balance, financials.Balance);
            if (row.IsPurchased)
                purchased = OrderRules.AddAmounts(purchased, financials.OrderTotal);
            else
                unpurchased = OrderRules.AddAmounts(unpurchased, financials.OrderTotal);
        }

        return new OrdersResponse(
            views,
            new OrderSummary(
                OrderRules.Money(all),
                OrderRules.Money(unpurchased),
                OrderRules.Money(purchased),
                OrderRules.Money(received),
                OrderRules.Money(balance)));
    }

    public async Task<OrderDetailResponse> GetDetailAsync(
        string actorId,
        long id,
        CancellationToken cancellationToken)
    {
        var row = await GetRowAsync(actorId, id, cancellationToken);
        var entries = await paymentEntryStore.ListAsync(id, cancellationToken);
        var activities = await activityStore.ListAsync(id, actorId, cancellationToken);
        return new OrderDetailResponse(
            ToView(row, actorId),
            entries.Select(ToView).ToArray(),
            activities.Select(ToView).ToArray());
    }

    public async Task<OrderView> GetAsync(
        string actorId,
        long id,
        CancellationToken cancellationToken)
    {
        var row = await GetRowAsync(actorId, id, cancellationToken);
        return ToView(row, actorId);
    }

    public async Task UpdateAsync(
        string actorId,
        long id,
        UpdateOrderCommand command,
        CancellationToken cancellationToken)
    {
        var before = await GetRowAsync(actorId, id, cancellationToken);
        OrderRules.ValidateOrderText(command.ItemName, command.Note);
        OrderRules.CalculateTotal(command.UnitPrice, command.Quantity);
        if (!await orderStore.UpdateAsync(id, actorId, command, cancellationToken))
            throw MissingOrder();

        var after = await GetAsync(actorId, id, cancellationToken);
        await RecordActivityAsync(
            id,
            actorId,
            ActorName(after, actorId),
            "更新訂單",
            BuildOrderChangeDetail(before, after),
            cancellationToken);
    }

    public async Task UpdatePurchaseStatusAsync(
        string actorId,
        long id,
        bool isPurchased,
        CancellationToken cancellationToken)
    {
        var before = await GetRowAsync(actorId, id, cancellationToken);
        if (!await orderStore.UpdatePurchaseStatusAsync(id, actorId, isPurchased, cancellationToken))
            throw MissingOrder();

        var after = await GetAsync(actorId, id, cancellationToken);
        if (before.IsPurchased != after.IsPurchased)
        {
            await RecordActivityAsync(
                id,
                actorId,
                ActorName(after, actorId),
                "購買狀態變更",
                $"購買狀態：{PurchaseStatus(before.IsPurchased)} → {PurchaseStatus(after.IsPurchased)}。",
                cancellationToken);
            await notificationService.NotifyPurchaseStatusAsync(after, actorId);
        }
    }

    public async Task UpdateSettlementModeAsync(
        string actorId,
        long id,
        string settlementMode,
        CancellationToken cancellationToken)
    {
        if (!SettlementModes.IsValid(settlementMode))
            throw new OrderRuleException("收款完成模式不正確。");
        var before = await GetRowAsync(actorId, id, cancellationToken);
        if (!await orderStore.UpdateSettlementModeAsync(id, actorId, settlementMode, cancellationToken))
            throw MissingOrder();

        var after = await GetAsync(actorId, id, cancellationToken);
        if ((before.SettlementOverride ?? SettlementModes.Auto) != after.SettlementMode
            || OrderRules.CalculateFinancials(
                before.UnitPrice,
                before.Quantity,
                before.ReceivedTotal,
                before.SettlementOverride).IsSettlementComplete != after.IsSettlementComplete)
        {
            await RecordActivityAsync(
                id,
                actorId,
                ActorName(after, actorId),
                "收款狀態變更",
                $"收款狀態：{SettlementStatus(before)} → {SettlementStatus(after)}。",
                cancellationToken);
            await notificationService.NotifySettlementStatusAsync(after, actorId);
        }
    }

    public async Task ArchiveAsync(string actorId, long id, CancellationToken cancellationToken)
    {
        var before = await GetRowAsync(actorId, id, cancellationToken);
        if (!await orderStore.ArchiveAsync(id, actorId, cancellationToken))
            throw MissingOrder();
        await RecordActivityAsync(
            id,
            actorId,
            ActorName(before, actorId),
            "封存訂單",
            "訂單已封存。",
            cancellationToken);
    }

    public async Task RestoreAsync(string actorId, long id, CancellationToken cancellationToken)
    {
        var before = await GetRowAsync(actorId, id, cancellationToken);
        if (!await orderStore.RestoreAsync(id, actorId, cancellationToken))
            throw MissingOrder();
        await RecordActivityAsync(
            id,
            actorId,
            ActorName(before, actorId),
            "復原訂單",
            "訂單已復原。",
            cancellationToken);
    }

    public async Task<PaymentMutationResult> AddPaymentAsync(
        string actorId,
        long orderId,
        long amount,
        string reason,
        CancellationToken cancellationToken)
    {
        OrderRules.ValidatePayment(amount, reason);
        var result = await paymentEntryStore.AddAsync(orderId, actorId, amount, reason, cancellationToken);
        if (result is null)
            throw MissingOrder();
        var order = await GetAsync(actorId, result.OrderId, cancellationToken);
        await RecordActivityAsync(
            result.OrderId,
            actorId,
            ActorName(order, actorId),
            "新增款項紀錄",
            $"新增款項：NT$ {OrderRules.Money(amount)}，事由：{reason}。",
            cancellationToken);
        await notificationService.NotifyPaymentChangedAsync(
            order,
            actorId,
            "新增款項紀錄",
            $"新增款項：NT$ {OrderRules.Money(amount)}，事由：{reason}。");
        return result;
    }

    public async Task<PaymentMutationResult> UpdatePaymentAsync(
        string actorId,
        long paymentId,
        long amount,
        string reason,
        CancellationToken cancellationToken)
    {
        OrderRules.ValidatePayment(amount, reason);
        var result = await paymentEntryStore.UpdateAsync(paymentId, actorId, amount, reason, cancellationToken);
        if (result is null)
            throw MissingOrder();
        var order = await GetAsync(actorId, result.OrderId, cancellationToken);
        await RecordActivityAsync(
            result.OrderId,
            actorId,
            ActorName(order, actorId),
            "修改款項紀錄",
            $"修改款項 #{paymentId}：金額 NT$ {OrderRules.Money(amount)}，事由：{reason}。",
            cancellationToken);
        await notificationService.NotifyPaymentChangedAsync(
            order,
            actorId,
            "修改款項紀錄",
            $"修改款項 #{paymentId}：金額 NT$ {OrderRules.Money(amount)}，事由：{reason}。");
        return result;
    }

    public async Task<PaymentMutationResult> DeletePaymentAsync(
        string actorId,
        long paymentId,
        CancellationToken cancellationToken)
    {
        var result = await paymentEntryStore.DeleteAsync(paymentId, actorId, cancellationToken);
        if (result is null)
            throw MissingOrder();
        var order = await GetAsync(actorId, result.OrderId, cancellationToken);
        await RecordActivityAsync(
            result.OrderId,
            actorId,
            ActorName(order, actorId),
            "刪除款項紀錄",
            $"刪除款項 #{paymentId}。",
            cancellationToken);
        await notificationService.NotifyPaymentChangedAsync(
            order,
            actorId,
            "刪除款項紀錄",
            $"刪除款項 #{paymentId}。");
        return result;
    }

    public async Task<IReadOnlyList<UserBlockView>> ListBlocksAsync(
        string actorId,
        CancellationToken cancellationToken)
    {
        var rows = await userBlockStore.ListAsync(actorId, cancellationToken);
        return rows.Select(row => new UserBlockView(
            row.RequesterDiscordUserId,
            row.RequesterDisplayName,
            OrderRules.Utc(row.CreatedAt))).ToArray();
    }

    public async Task BlockAsync(
        string actorId,
        string requesterId,
        string requesterDisplayName,
        CancellationToken cancellationToken)
    {
        ValidateDiscordId(actorId, "代購方 UID");
        ValidateDiscordId(requesterId, "請求方 UID");
        if (actorId == requesterId)
            throw new OrderRuleException("不可封鎖自己。");
        if (string.IsNullOrWhiteSpace(requesterDisplayName))
            requesterDisplayName = requesterId;
        await userBlockStore.AddAsync(actorId, requesterId, requesterDisplayName.Trim(), cancellationToken);
    }

    public async Task UnblockAsync(
        string actorId,
        string requesterId,
        CancellationToken cancellationToken)
    {
        ValidateDiscordId(actorId, "代購方 UID");
        ValidateDiscordId(requesterId, "請求方 UID");
        await userBlockStore.RemoveAsync(actorId, requesterId, cancellationToken);
    }

    private async Task<OrderRow> GetRowAsync(
        string actorId,
        long id,
        CancellationToken cancellationToken)
    {
        ValidateDiscordId(actorId, "操作者 UID");
        if (id <= 0)
            throw MissingOrder();
        return await orderStore.FindAccessibleAsync(actorId, id, cancellationToken) ?? throw MissingOrder();
    }

    private static OrderView ToView(OrderRow row, string actorId)
    {
        var financials = OrderRules.CalculateFinancials(
            row.UnitPrice,
            row.Quantity,
            row.ReceivedTotal,
            row.SettlementOverride);
        var isBuyer = row.BuyerDiscordUserId == actorId;
        var isRequester = row.RequesterDiscordUserId == actorId;
        var active = row.ArchivedAt is null;
        var canArchive = active && (isBuyer || (isRequester && row.PaymentCount == 0));
        var canRestore = !active && (isBuyer || (isRequester && row.PaymentCount == 0));

        return new OrderView(
            row.Id.ToString(),
            row.RequesterDiscordUserId,
            row.RequesterDisplayName,
            row.BuyerDiscordUserId,
            row.BuyerDisplayName,
            row.SourceGuildId,
            row.SourceGuildName,
            row.ItemName,
            OrderRules.Money(row.UnitPrice),
            row.Quantity,
            row.Note,
            row.Stall,
            row.IsPurchased,
            OrderRules.Utc(row.PurchasedAt),
            row.SettlementOverride ?? SettlementModes.Auto,
            OrderRules.Utc(row.CreatedAt),
            OrderRules.Utc(row.UpdatedAt),
            OrderRules.Utc(row.ArchivedAt),
            OrderRules.Money(financials.OrderTotal),
            OrderRules.Money(financials.ReceivedTotal),
            OrderRules.Money(financials.Balance),
            financials.IsSettlementComplete,
            new OrderPermissions(active && (isBuyer || isRequester), active && isBuyer, canArchive, canRestore));
    }

    private static PaymentEntryView ToView(PaymentEntryRow row) => new(
        row.Id.ToString(),
        row.OrderId.ToString(),
        OrderRules.Money(row.Amount),
        row.Reason,
        OrderRules.Utc(row.CreatedAt),
        OrderRules.Utc(row.UpdatedAt));

    private static OrderActivityView ToView(OrderActivityRow row) => new(
        row.Id.ToString(),
        row.OrderId.ToString(),
        row.ActorDiscordUserId,
        row.ActorDisplayName,
        row.ActionType,
        row.Detail,
        OrderRules.Utc(row.CreatedAt));

    private async Task RecordActivityAsync(
        long orderId,
        string actorId,
        string actorDisplayName,
        string actionType,
        string detail,
        CancellationToken cancellationToken)
    {
        try
        {
            // ponytail: record after the mutation; use one shared transaction if audit durability becomes mandatory.
            await activityStore.AddAsync(
                orderId,
                actorId,
                actorDisplayName,
                actionType,
                detail,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not record order activity for order {OrderId}.", orderId);
        }
    }

    private static string BuildOrderChangeDetail(OrderRow before, OrderView after)
    {
        var changes = new List<string>();
        if (before.ItemName != after.ItemName)
            changes.Add($"商品：{before.ItemName} → {after.ItemName}");
        if (OrderRules.Money(before.UnitPrice) != after.UnitPrice)
            changes.Add($"單價：NT$ {before.UnitPrice} → NT$ {after.UnitPrice}");
        if (before.Quantity != after.Quantity)
            changes.Add($"數量：{before.Quantity} → {after.Quantity}");
        if (before.Note != after.Note)
            changes.Add("備註已變更");
        if (before.Stall != after.Stall)
            changes.Add($"攤位：{before.Stall ?? "未填寫"} → {after.Stall ?? "未填寫"}");
        return changes.Count == 0 ? "訂單內容未變更。" : string.Join("；", changes) + "。";
    }

    private static string ActorName(OrderRow row, string actorId) =>
        row.RequesterDiscordUserId == actorId ? row.RequesterDisplayName
        : row.BuyerDiscordUserId == actorId ? row.BuyerDisplayName
        : actorId;

    private static string ActorName(OrderView order, string actorId) =>
        order.RequesterDiscordUserId == actorId ? order.RequesterDisplayName
        : order.BuyerDiscordUserId == actorId ? order.BuyerDisplayName
        : actorId;

    private static string PurchaseStatus(bool isPurchased) => isPurchased ? "已購買" : "尚未購買";

    private static string SettlementStatus(OrderRow row)
    {
        var financials = OrderRules.CalculateFinancials(
            row.UnitPrice,
            row.Quantity,
            row.ReceivedTotal,
            row.SettlementOverride);
        return financials.IsSettlementComplete ? "已完成" : "未完成";
    }

    private static string SettlementStatus(OrderView order) =>
        order.IsSettlementComplete ? "已完成" : "未完成";

    private static OrderNotFoundException MissingOrder() =>
        new("找不到該筆訂單，可能已被封存或你沒有操作權限。");

    private static void ValidateDiscordId(string value, string label)
    {
        if (!ulong.TryParse(value, out var parsed) || parsed == 0)
            throw new OrderRuleException($"「{label}」格式不正確。");
    }
}
