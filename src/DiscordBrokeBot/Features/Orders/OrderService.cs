using DiscordBrokeBot.Features.Orders.Models;

namespace DiscordBrokeBot.Features.Orders;

/// <summary>Applies shared order, amount, settlement, and UID authorization rules.</summary>
/// <remarks>
/// Both Discord interactions and HTTP endpoints call this service. Stores remain the only SQL
/// boundary, so changing the future persistence implementation does not change either contract.
/// </remarks>
public sealed class OrderService(
    OrderStore orderStore,
    PaymentEntryStore paymentEntryStore,
    UserBlockStore userBlockStore)
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

        return await GetAsync(command.RequesterDiscordUserId, id.Value, cancellationToken);
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
        return new OrderDetailResponse(
            ToView(row, actorId),
            entries.Select(ToView).ToArray());
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
        OrderRules.ValidateOrderText(command.ItemName, command.Note);
        OrderRules.CalculateTotal(command.UnitPrice, command.Quantity);
        if (!await orderStore.UpdateAsync(id, actorId, command, cancellationToken))
            throw MissingOrder();
    }

    public async Task UpdatePurchaseStatusAsync(
        string actorId,
        long id,
        bool isPurchased,
        CancellationToken cancellationToken)
    {
        if (!await orderStore.UpdatePurchaseStatusAsync(id, actorId, isPurchased, cancellationToken))
            throw MissingOrder();
    }

    public async Task UpdateSettlementModeAsync(
        string actorId,
        long id,
        string settlementMode,
        CancellationToken cancellationToken)
    {
        if (!SettlementModes.IsValid(settlementMode))
            throw new OrderRuleException("收款完成模式不正確。");
        if (!await orderStore.UpdateSettlementModeAsync(id, actorId, settlementMode, cancellationToken))
            throw MissingOrder();
    }

    public async Task ArchiveAsync(string actorId, long id, CancellationToken cancellationToken)
    {
        if (!await orderStore.ArchiveAsync(id, actorId, cancellationToken))
            throw MissingOrder();
    }

    public async Task RestoreAsync(string actorId, long id, CancellationToken cancellationToken)
    {
        if (!await orderStore.RestoreAsync(id, actorId, cancellationToken))
            throw MissingOrder();
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
        return result ?? throw MissingOrder();
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
        return result ?? throw MissingOrder();
    }

    public async Task<PaymentMutationResult> DeletePaymentAsync(
        string actorId,
        long paymentId,
        CancellationToken cancellationToken)
    {
        var result = await paymentEntryStore.DeleteAsync(paymentId, actorId, cancellationToken);
        return result ?? throw MissingOrder();
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

    private static OrderNotFoundException MissingOrder() =>
        new("找不到該筆訂單，可能已被封存或你沒有操作權限。");

    private static void ValidateDiscordId(string value, string label)
    {
        if (!ulong.TryParse(value, out var parsed) || parsed == 0)
            throw new OrderRuleException($"{label}格式不正確。");
    }
}
