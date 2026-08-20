using System.Globalization;

namespace DiscordBrokeBot.Features.Orders.Models;

public static class SettlementModes
{
    public const string Auto = "auto";
    public const string ForceCompleted = "force_completed";
    public const string ForcePending = "force_pending";

    public static bool IsValid(string value) => value is Auto or ForceCompleted or ForcePending;
}

public class OrderRuleException(string message) : Exception(message);

public sealed class OrderNotFoundException(string message) : OrderRuleException(message);

public sealed record CreateOrderCommand(
    string RequesterDiscordUserId,
    string RequesterDisplayName,
    string BuyerDiscordUserId,
    string BuyerDisplayName,
    string SourceGuildId,
    string SourceGuildName,
    string ItemName,
    long UnitPrice,
    int Quantity,
    string Note,
    string? Stall);

public sealed record UpdateOrderCommand(
    string ItemName,
    long UnitPrice,
    int Quantity,
    string Note,
    string? Stall);

public sealed record OrderRow
{
    public long Id { get; init; }
    public string RequesterDiscordUserId { get; init; } = "";
    public string RequesterDisplayName { get; init; } = "";
    public string BuyerDiscordUserId { get; init; } = "";
    public string BuyerDisplayName { get; init; } = "";
    public string SourceGuildId { get; init; } = "";
    public string SourceGuildName { get; init; } = "";
    public string ItemName { get; init; } = "";
    public long UnitPrice { get; init; }
    public int Quantity { get; init; }
    public string Note { get; init; } = "";
    public string? Stall { get; init; }
    public bool IsPurchased { get; init; }
    public DateTime? PurchasedAt { get; init; }
    public string? SettlementOverride { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? ArchivedAt { get; init; }
    public string? ArchivedByDiscordUserId { get; init; }
    public long ReceivedTotal { get; init; }
    public long PaymentCount { get; init; }
}

public sealed record PaymentEntryRow
{
    public long Id { get; init; }
    public long OrderId { get; init; }
    public long Amount { get; init; }
    public string Reason { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record UserBlockRow
{
    public string BuyerDiscordUserId { get; init; } = "";
    public string RequesterDiscordUserId { get; init; } = "";
    public string RequesterDisplayName { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

public sealed record OrderPermissions(
    bool CanEdit,
    bool CanManageBuyerActions,
    bool CanArchive,
    bool CanRestore);

public sealed record OrderFinancials(
    long OrderTotal,
    long ReceivedTotal,
    long Balance,
    bool IsSettlementComplete);

public sealed record OrderView(
    string Id,
    string RequesterDiscordUserId,
    string RequesterDisplayName,
    string BuyerDiscordUserId,
    string BuyerDisplayName,
    string SourceGuildId,
    string SourceGuildName,
    string ItemName,
    string UnitPrice,
    int Quantity,
    string Note,
    string? Stall,
    bool IsPurchased,
    DateTime? PurchasedAt,
    string SettlementMode,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ArchivedAt,
    string OrderTotal,
    string ReceivedTotal,
    string Balance,
    bool IsSettlementComplete,
    OrderPermissions Permissions);

public sealed record PaymentEntryView(
    string Id,
    string OrderId,
    string Amount,
    string Reason,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record OrderSummary(
    string AllOrderTotal,
    string UnpurchasedOrderTotal,
    string PurchasedOrderTotal,
    string ReceivedTotal,
    string BalanceTotal);

public sealed record OrdersResponse(IReadOnlyList<OrderView> Orders, OrderSummary Summary);

public sealed record OrderDetailResponse(
    OrderView Order,
    IReadOnlyList<PaymentEntryView> PaymentEntries);

public sealed record UserBlockView(
    string RequesterDiscordUserId,
    string RequesterDisplayName,
    DateTime CreatedAt);

public sealed record PaymentMutationResult(
    long OrderId,
    string RequesterDiscordUserId,
    bool WasSettlementComplete,
    bool IsSettlementComplete);

public static class OrderRules
{
    public static long CalculateTotal(long unitPrice, int quantity)
    {
        if (unitPrice <= 0)
            throw new OrderRuleException("單價必須大於零。");
        if (quantity <= 0)
            throw new OrderRuleException("數量必須大於零。");

        try
        {
            return checked(unitPrice * quantity);
        }
        catch (OverflowException)
        {
            throw new OrderRuleException("訂單總額超過可支援的整數範圍。");
        }
    }

    public static void ValidateOrderText(string itemName, string note)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            throw new OrderRuleException("物品名稱為必填。");
        if (string.IsNullOrWhiteSpace(note))
            throw new OrderRuleException("備註為必填。");
    }

    public static void ValidatePayment(long amount, string reason)
    {
        if (amount == 0)
            throw new OrderRuleException("款項金額不可為零。");
        if (string.IsNullOrWhiteSpace(reason))
            throw new OrderRuleException("款項事由為必填。");
    }

    public static OrderFinancials CalculateFinancials(
        long unitPrice,
        int quantity,
        long receivedTotal,
        string? settlementOverride)
    {
        var total = CalculateTotal(unitPrice, quantity);
        var complete = settlementOverride switch
        {
            SettlementModes.ForceCompleted => true,
            SettlementModes.ForcePending => false,
            _ => receivedTotal >= total,
        };
        return new OrderFinancials(total, receivedTotal, SubtractAmounts(total, receivedTotal), complete);
    }

    public static long AddAmounts(long left, long right) => CheckedAmount(left, right, add: true);

    public static long SubtractAmounts(long left, long right) => CheckedAmount(left, right, add: false);

    public static string Money(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static DateTime? Utc(DateTime? value) => value is null ? null : Utc(value.Value);

    public static DateTime Utc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static long CheckedAmount(long left, long right, bool add)
    {
        try
        {
            return add ? checked(left + right) : checked(left - right);
        }
        catch (OverflowException)
        {
            throw new OrderRuleException("金額超過可支援的整數範圍。");
        }
    }
}
