namespace DiscordBrokeBot.Api;

public sealed record UpdateOrderRequest(
    string ItemName,
    string UnitPrice,
    int Quantity,
    string Note,
    string? Stall);

public sealed record PurchaseStatusRequest(bool IsPurchased);

public sealed record SettlementModeRequest(string SettlementMode);

public sealed record PaymentEntryRequest(string Amount, string Reason);

public sealed record AuthUserResponse(
    string DiscordUserId,
    string DisplayName,
    string? AvatarUrl);

public sealed record ApiError(string Message);
