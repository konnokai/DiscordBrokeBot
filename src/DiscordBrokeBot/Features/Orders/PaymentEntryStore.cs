using Dapper;
using DiscordBrokeBot.Features.Orders.Models;
using DiscordBrokeBot.Infrastructure.Database;

namespace DiscordBrokeBot.Features.Orders;

/// <summary>Stores payment entries and protects their order-level financial recalculation.</summary>
/// <remarks>
/// Each mutation locks the order row with MariaDB <c>FOR UPDATE</c> before changing the entry.
/// This keeps payment totals and settlement transitions atomic; an EF Core migration must retain
/// that lock and transaction behavior.
/// </remarks>
public sealed class PaymentEntryStore(MySqlConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<PaymentEntryRow>> ListAsync(
        long orderId,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PaymentEntryRow>(new CommandDefinition(
            """
            SELECT
                id AS Id,
                order_id AS OrderId,
                amount AS Amount,
                reason AS Reason,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM payment_entries
            WHERE order_id = @OrderId
            ORDER BY created_at DESC, id DESC;
            """,
            new { OrderId = orderId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<PaymentMutationResult?> AddAsync(
        long orderId,
        string actorId,
        long amount,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var order = await LoadOrderAsync(connection, transaction, orderId, cancellationToken);
        if (order is null || order.ArchivedAt is not null || order.BuyerDiscordUserId != actorId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var beforeTotal = await SumAsync(connection, transaction, orderId, cancellationToken);
        var wasComplete = OrderRules.CalculateFinancials(
            order.UnitPrice,
            order.Quantity,
            beforeTotal,
            order.SettlementOverride).IsSettlementComplete;

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO payment_entries (order_id, amount, reason, created_at, updated_at)
            VALUES (@OrderId, @Amount, @Reason, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
            """,
            new { OrderId = orderId, Amount = amount, Reason = reason },
            transaction,
            cancellationToken: cancellationToken));
        var afterTotal = OrderRules.AddAmounts(beforeTotal, amount);
        var isComplete = OrderRules.CalculateFinancials(
            order.UnitPrice,
            order.Quantity,
            afterTotal,
            order.SettlementOverride).IsSettlementComplete;

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE orders SET updated_at = UTC_TIMESTAMP(6) WHERE id = @OrderId;",
            new { OrderId = orderId },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new PaymentMutationResult(orderId, order.RequesterDiscordUserId, wasComplete, isComplete);
    }

    public async Task<PaymentMutationResult?> UpdateAsync(
        long paymentId,
        string actorId,
        long amount,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var payment = await LoadPaymentAsync(connection, transaction, paymentId, cancellationToken);
        if (payment is null || payment.ArchivedAt is not null || payment.BuyerDiscordUserId != actorId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var beforeTotal = await SumAsync(connection, transaction, payment.OrderId, cancellationToken);
        var wasComplete = OrderRules.CalculateFinancials(
            payment.UnitPrice,
            payment.Quantity,
            beforeTotal,
            payment.SettlementOverride).IsSettlementComplete;
        var afterTotal = OrderRules.AddAmounts(
            OrderRules.SubtractAmounts(beforeTotal, payment.Amount),
            amount);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE payment_entries
            SET amount = @Amount,
                reason = @Reason,
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @PaymentId;
            """,
            new { PaymentId = paymentId, Amount = amount, Reason = reason },
            transaction,
            cancellationToken: cancellationToken));
        var isComplete = OrderRules.CalculateFinancials(
            payment.UnitPrice,
            payment.Quantity,
            afterTotal,
            payment.SettlementOverride).IsSettlementComplete;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE orders SET updated_at = UTC_TIMESTAMP(6) WHERE id = @OrderId;",
            new { payment.OrderId },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new PaymentMutationResult(payment.OrderId, payment.RequesterDiscordUserId, wasComplete, isComplete);
    }

    public async Task<PaymentMutationResult?> DeleteAsync(
        long paymentId,
        string actorId,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var payment = await LoadPaymentAsync(connection, transaction, paymentId, cancellationToken);
        if (payment is null || payment.ArchivedAt is not null || payment.BuyerDiscordUserId != actorId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var beforeTotal = await SumAsync(connection, transaction, payment.OrderId, cancellationToken);
        var wasComplete = OrderRules.CalculateFinancials(
            payment.UnitPrice,
            payment.Quantity,
            beforeTotal,
            payment.SettlementOverride).IsSettlementComplete;
        var afterTotal = OrderRules.SubtractAmounts(beforeTotal, payment.Amount);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM payment_entries WHERE id = @PaymentId;",
            new { PaymentId = paymentId },
            transaction,
            cancellationToken: cancellationToken));
        var isComplete = OrderRules.CalculateFinancials(
            payment.UnitPrice,
            payment.Quantity,
            afterTotal,
            payment.SettlementOverride).IsSettlementComplete;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE orders SET updated_at = UTC_TIMESTAMP(6) WHERE id = @OrderId;",
            new { payment.OrderId },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new PaymentMutationResult(payment.OrderId, payment.RequesterDiscordUserId, wasComplete, isComplete);
    }

    private static async Task<long> SumAsync(
        MySqlConnector.MySqlConnection connection,
        MySqlConnector.MySqlTransaction transaction,
        long orderId,
        CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE(SUM(amount), 0) FROM payment_entries WHERE order_id = @OrderId;",
            new { OrderId = orderId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task<LockedOrder?> LoadOrderAsync(
        MySqlConnector.MySqlConnection connection,
        MySqlConnector.MySqlTransaction transaction,
        long orderId,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<LockedOrder>(new CommandDefinition(
            """
            SELECT
                id AS Id,
                requester_discord_user_id AS RequesterDiscordUserId,
                buyer_discord_user_id AS BuyerDiscordUserId,
                unit_price AS UnitPrice,
                quantity AS Quantity,
                settlement_override AS SettlementOverride,
                archived_at AS ArchivedAt
            FROM orders
            WHERE id = @OrderId
            FOR UPDATE;
            """,
            new { OrderId = orderId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task<LockedPayment?> LoadPaymentAsync(
        MySqlConnector.MySqlConnection connection,
        MySqlConnector.MySqlTransaction transaction,
        long paymentId,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<LockedPayment>(new CommandDefinition(
            """
            SELECT
                p.id AS Id,
                p.order_id AS OrderId,
                p.amount AS Amount,
                o.requester_discord_user_id AS RequesterDiscordUserId,
                o.buyer_discord_user_id AS BuyerDiscordUserId,
                o.unit_price AS UnitPrice,
                o.quantity AS Quantity,
                o.settlement_override AS SettlementOverride,
                o.archived_at AS ArchivedAt
            FROM payment_entries p
            INNER JOIN orders o ON o.id = p.order_id
            WHERE p.id = @PaymentId
            FOR UPDATE;
            """,
            new { PaymentId = paymentId },
            transaction,
            cancellationToken: cancellationToken));
    }

    private sealed record LockedOrder
    {
        public long Id { get; init; }
        public string RequesterDiscordUserId { get; init; } = "";
        public string BuyerDiscordUserId { get; init; } = "";
        public long UnitPrice { get; init; }
        public int Quantity { get; init; }
        public string? SettlementOverride { get; init; }
        public DateTime? ArchivedAt { get; init; }
    }

    private sealed record LockedPayment
    {
        public long Id { get; init; }
        public long OrderId { get; init; }
        public long Amount { get; init; }
        public string RequesterDiscordUserId { get; init; } = "";
        public string BuyerDiscordUserId { get; init; } = "";
        public long UnitPrice { get; init; }
        public int Quantity { get; init; }
        public string? SettlementOverride { get; init; }
        public DateTime? ArchivedAt { get; init; }
    }
}
