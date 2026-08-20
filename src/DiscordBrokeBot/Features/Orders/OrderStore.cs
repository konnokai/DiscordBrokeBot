using Dapper;
using DiscordBrokeBot.Features.Orders.Models;
using DiscordBrokeBot.Infrastructure.Database;

namespace DiscordBrokeBot.Features.Orders;

/// <summary>Persists order rows and their authorization predicates with Dapper.</summary>
/// <remarks>
/// SQL stays in this store so the Bot and Web API share the same atomic behavior. MariaDB
/// row locks and affected-row checks are intentional; the future EF Core mapping must keep
/// the same predicates and transaction boundaries documented in docs/EF_CORE_MIGRATION.md.
/// </remarks>
public sealed class OrderStore(MySqlConnectionFactory connectionFactory)
{
    private const string SelectOrder = """
        SELECT
            o.id AS Id,
            o.requester_discord_user_id AS RequesterDiscordUserId,
            o.requester_display_name AS RequesterDisplayName,
            o.buyer_discord_user_id AS BuyerDiscordUserId,
            o.buyer_display_name AS BuyerDisplayName,
            o.source_guild_id AS SourceGuildId,
            o.source_guild_name AS SourceGuildName,
            o.item_name AS ItemName,
            o.unit_price AS UnitPrice,
            o.quantity AS Quantity,
            o.note AS Note,
            o.stall AS Stall,
            o.is_purchased AS IsPurchased,
            o.purchased_at AS PurchasedAt,
            o.settlement_override AS SettlementOverride,
            o.created_at AS CreatedAt,
            o.updated_at AS UpdatedAt,
            o.archived_at AS ArchivedAt,
            o.archived_by_discord_user_id AS ArchivedByDiscordUserId,
            COALESCE(p.ReceivedTotal, 0) AS ReceivedTotal,
            COALESCE(p.PaymentCount, 0) AS PaymentCount
        FROM orders o
        LEFT JOIN (
            SELECT
                order_id,
                SUM(amount) AS ReceivedTotal,
                COUNT(*) AS PaymentCount
            FROM payment_entries
            GROUP BY order_id
        ) p ON p.order_id = o.id
        """;

    /// <summary>Creates an order only if the buyer has not blocked the requester.</summary>
    /// <remarks>
    /// The block check and insert use one transaction. This prevents a concurrent block from
    /// being bypassed between two independent queries.
    /// </remarks>
    public async Task<long?> CreateIfNotBlockedAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var blocked = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM user_blocks
            WHERE buyer_discord_user_id = @BuyerDiscordUserId
              AND requester_discord_user_id = @RequesterDiscordUserId;
            """,
            command,
            transaction,
            cancellationToken: cancellationToken));

        if (blocked > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO orders (
                requester_discord_user_id, requester_display_name,
                buyer_discord_user_id, buyer_display_name,
                source_guild_id, source_guild_name,
                item_name, unit_price, quantity, note, stall,
                is_purchased, settlement_override, created_at, updated_at
            ) VALUES (
                @RequesterDiscordUserId, @RequesterDisplayName,
                @BuyerDiscordUserId, @BuyerDisplayName,
                @SourceGuildId, @SourceGuildName,
                @ItemName, @UnitPrice, @Quantity, @Note, @Stall,
                0, NULL, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
            );
            """,
            command,
            transaction,
            cancellationToken: cancellationToken));

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT LAST_INSERT_ID();",
            transaction: transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<OrderRow>> ListAsync(
        string actorId,
        string role,
        bool archived,
        CancellationToken cancellationToken)
    {
        var ownerColumn = role == "buyer" ? "o.buyer_discord_user_id" : "o.requester_discord_user_id";
        var archivePredicate = archived ? "o.archived_at IS NOT NULL" : "o.archived_at IS NULL";
        var sql = $"""
            {SelectOrder}
            WHERE {ownerColumn} = @ActorId
              AND {archivePredicate}
            ORDER BY o.created_at DESC, o.id DESC;
            """;

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<OrderRow>(new CommandDefinition(
            sql,
            new { ActorId = actorId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<OrderRow?> FindAccessibleAsync(
        string actorId,
        long id,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition(
            $"""
            {SelectOrder}
            WHERE o.id = @Id
              AND (o.requester_discord_user_id = @ActorId OR o.buyer_discord_user_id = @ActorId)
            ;
            """,
            new { Id = id, ActorId = actorId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(
        long id,
        string actorId,
        UpdateOrderCommand command,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE orders
            SET item_name = @ItemName,
                unit_price = @UnitPrice,
                quantity = @Quantity,
                note = @Note,
                stall = @Stall,
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @Id
              AND archived_at IS NULL
              AND (requester_discord_user_id = @ActorId OR buyer_discord_user_id = @ActorId);
            """,
            new
            {
                Id = id,
                ActorId = actorId,
                command.ItemName,
                command.UnitPrice,
                command.Quantity,
                command.Note,
                command.Stall,
            },
            cancellationToken: cancellationToken));
        return affected == 1;
    }

    public async Task<bool> UpdatePurchaseStatusAsync(
        long id,
        string actorId,
        bool isPurchased,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE orders
            SET is_purchased = @IsPurchased,
                purchased_at = CASE WHEN @IsPurchased = 1 THEN UTC_TIMESTAMP(6) ELSE NULL END,
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @Id
              AND buyer_discord_user_id = @ActorId
              AND archived_at IS NULL;
            """,
            new { Id = id, ActorId = actorId, IsPurchased = isPurchased },
            cancellationToken: cancellationToken));
        return affected == 1;
    }

    public async Task<bool> UpdateSettlementModeAsync(
        long id,
        string actorId,
        string settlementMode,
        CancellationToken cancellationToken)
    {
        var overrideValue = settlementMode == SettlementModes.Auto ? null : settlementMode;
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE orders
            SET settlement_override = @OverrideValue,
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @Id
              AND buyer_discord_user_id = @ActorId
              AND archived_at IS NULL;
            """,
            new { Id = id, ActorId = actorId, OverrideValue = overrideValue },
            cancellationToken: cancellationToken));
        return affected == 1;
    }

    public async Task<bool> ArchiveAsync(long id, string actorId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var state = await connection.QuerySingleOrDefaultAsync<ArchiveState>(new CommandDefinition(
            """
            SELECT
                o.requester_discord_user_id AS RequesterDiscordUserId,
                o.buyer_discord_user_id AS BuyerDiscordUserId,
                o.archived_at AS ArchivedAt,
                (SELECT COUNT(*) FROM payment_entries p WHERE p.order_id = o.id) AS PaymentCount
            FROM orders o
            WHERE o.id = @Id
            FOR UPDATE;
            """,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));

        if (state is null || state.ArchivedAt is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var isBuyer = state.BuyerDiscordUserId == actorId;
        var isRequester = state.RequesterDiscordUserId == actorId;
        if ((!isBuyer && !isRequester) || (!isBuyer && state.PaymentCount > 0))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE orders
            SET archived_at = UTC_TIMESTAMP(6),
                archived_by_discord_user_id = @ActorId,
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @Id AND archived_at IS NULL;
            """,
            new { Id = id, ActorId = actorId },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return affected == 1;
    }

    public async Task<bool> RestoreAsync(long id, string actorId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var state = await connection.QuerySingleOrDefaultAsync<ArchiveState>(new CommandDefinition(
            """
            SELECT
                o.requester_discord_user_id AS RequesterDiscordUserId,
                o.buyer_discord_user_id AS BuyerDiscordUserId,
                o.archived_at AS ArchivedAt,
                (SELECT COUNT(*) FROM payment_entries p WHERE p.order_id = o.id) AS PaymentCount
            FROM orders o
            WHERE o.id = @Id
            FOR UPDATE;
            """,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));

        if (state is null || state.ArchivedAt is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var isBuyer = state.BuyerDiscordUserId == actorId;
        var isRequester = state.RequesterDiscordUserId == actorId;
        if ((!isBuyer && !isRequester) || (!isBuyer && state.PaymentCount > 0))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE orders
            SET archived_at = NULL,
                archived_by_discord_user_id = NULL,
                updated_at = UTC_TIMESTAMP(6)
            WHERE id = @Id AND archived_at IS NOT NULL;
            """,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return affected == 1;
    }

    private sealed record ArchiveState
    {
        public string RequesterDiscordUserId { get; init; } = "";
        public string BuyerDiscordUserId { get; init; } = "";
        public DateTime? ArchivedAt { get; init; }
        public long PaymentCount { get; init; }
    }
}
