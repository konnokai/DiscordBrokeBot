using System.Globalization;
using DiscordBrokeBot.Auth;
using DiscordBrokeBot.Features.Orders;
using DiscordBrokeBot.Features.Orders.Models;

namespace DiscordBrokeBot.Api;

public static class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/auth/me", GetCurrentUser);
        api.MapGet("/auth/csrf", (HttpContext context, CsrfService csrf) =>
        {
            var userId = GetActorId(context);
            return Results.Ok(new { Token = csrf.CreateToken(userId) });
        });

        api.MapGet("/orders", async (
            HttpContext context,
            OrderService service,
            string? role,
            string? archived,
            CancellationToken cancellationToken) =>
        {
            var parsedArchived = ParseBoolean(archived, false, "封存條件");
            return Results.Ok(await service.ListAsync(
                GetActorId(context),
                role?.Trim().ToLowerInvariant() ?? "buyer",
                parsedArchived,
                cancellationToken));
        }).RequireRateLimiting("api-query");

        api.MapGet("/orders/{id}", async (
            string id,
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await service.GetDetailAsync(
                GetActorId(context),
                ParseId(id),
                cancellationToken));
        }).RequireRateLimiting("api-query");

        api.MapPatch("/orders/{id}", async (
            string id,
            UpdateOrderRequest request,
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            await service.UpdateAsync(
                GetActorId(context),
                ParseId(id),
                new UpdateOrderCommand(
                    request.ItemName,
                    ParsePositiveAmount(request.UnitPrice, "單價"),
                    request.Quantity,
                    request.Note,
                    string.IsNullOrWhiteSpace(request.Stall) ? null : request.Stall.Trim()),
                cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("api-mutation");

        api.MapPut("/orders/{id}/purchase-status", async (
            string id,
            PurchaseStatusRequest request,
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            await service.UpdatePurchaseStatusAsync(
                GetActorId(context),
                ParseId(id),
                request.IsPurchased,
                cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("api-mutation");

        api.MapPut("/orders/{id}/settlement-mode", async (
            string id,
            SettlementModeRequest request,
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            await service.UpdateSettlementModeAsync(
                GetActorId(context),
                ParseId(id),
                request.SettlementMode,
                cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("api-mutation");

        api.MapPost("/orders/{id}/archive", async (
            string id,
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            await service.ArchiveAsync(GetActorId(context), ParseId(id), cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("api-mutation");

        api.MapPost("/orders/{id}/restore", async (
            string id,
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            await service.RestoreAsync(GetActorId(context), ParseId(id), cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("api-mutation");

        api.MapPost("/orders/{id}/payment-entries", async (
            string id,
            PaymentEntryRequest request,
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            await service.AddPaymentAsync(
                GetActorId(context),
                ParseId(id),
                ParseNonZeroAmount(request.Amount),
                request.Reason,
                cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("api-mutation");

        api.MapPatch("/payment-entries/{id}", async (
            string id,
            PaymentEntryRequest request,
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            await service.UpdatePaymentAsync(
                GetActorId(context),
                ParseId(id),
                ParseNonZeroAmount(request.Amount),
                request.Reason,
                cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("api-mutation");

        api.MapDelete("/payment-entries/{id}", async (
            string id,
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeletePaymentAsync(GetActorId(context), ParseId(id), cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("api-mutation");

        api.MapGet("/blocks", async (
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListBlocksAsync(GetActorId(context), cancellationToken)))
            .RequireRateLimiting("api-query");

        api.MapPost("/blocks/{requesterId}", async (
            string requesterId,
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            await service.BlockAsync(GetActorId(context), requesterId, requesterId, cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("api-mutation");

        api.MapDelete("/blocks/{requesterId}", async (
            string requesterId,
            HttpContext context,
            OrderService service,
            CancellationToken cancellationToken) =>
        {
            await service.UnblockAsync(GetActorId(context), requesterId, cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("api-mutation");
    }

    private static IResult GetCurrentUser(HttpContext context)
    {
        var userId = GetActorId(context);
        return Results.Ok(new AuthUserResponse(
            userId,
            context.User.Identity?.Name ?? userId,
            context.User.FindFirst("discord_avatar_url")?.Value));
    }

    private static string GetActorId(HttpContext context) =>
        context.User.DiscordUserId()
        ?? throw new InvalidOperationException("Authenticated Discord UID is missing.");

    private static long ParseId(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new OrderNotFoundException("找不到該筆訂單，可能已被封存或你沒有操作權限。");
        return id;
    }

    private static long ParsePositiveAmount(string value, string label)
    {
        var parsed = ParseAmount(value, label);
        if (parsed <= 0)
            throw new OrderRuleException($"{label}必須大於零。");
        return parsed;
    }

    private static long ParseNonZeroAmount(string value)
    {
        var parsed = ParseAmount(value, "款項金額");
        if (parsed == 0)
            throw new OrderRuleException("款項金額不可為零。");
        return parsed;
    }

    private static long ParseAmount(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            || value.Trim() != value)
            throw new OrderRuleException($"{label}必須是整數字串。");
        return parsed;
    }

    private static bool ParseBoolean(string? value, bool fallback, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (bool.TryParse(value, out var parsed))
            return parsed;
        throw new OrderRuleException($"{label}格式不正確。");
    }
}
