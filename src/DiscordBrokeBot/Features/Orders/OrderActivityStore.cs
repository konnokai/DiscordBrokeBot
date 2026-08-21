using Dapper;
using DiscordBrokeBot.Features.Orders.Models;
using DiscordBrokeBot.Infrastructure.Database;

namespace DiscordBrokeBot.Features.Orders;

/// <summary>Stores the visible history of successful order operations.</summary>
public sealed class OrderActivityStore(MySqlConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<OrderActivityRow>> ListAsync(
        long orderId,
        string actorId,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<OrderActivityRow>(new CommandDefinition(
            """
            SELECT
                a.id AS Id,
                a.order_id AS OrderId,
                a.actor_discord_user_id AS ActorDiscordUserId,
                a.actor_display_name AS ActorDisplayName,
                a.action_type AS ActionType,
                a.detail AS Detail,
                a.created_at AS CreatedAt
            FROM order_activities a
            INNER JOIN orders o ON o.id = a.order_id
            WHERE a.order_id = @OrderId
              AND (o.requester_discord_user_id = @ActorId OR o.buyer_discord_user_id = @ActorId)
            ORDER BY a.created_at DESC, a.id DESC;
            """,
            new { OrderId = orderId, ActorId = actorId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task AddAsync(
        long orderId,
        string actorId,
        string actorDisplayName,
        string actionType,
        string detail,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO order_activities (
                order_id,
                actor_discord_user_id,
                actor_display_name,
                action_type,
                detail,
                created_at)
            VALUES (
                @OrderId,
                @ActorDiscordUserId,
                @ActorDisplayName,
                @ActionType,
                @Detail,
                UTC_TIMESTAMP(6));
            """,
            new
            {
                OrderId = orderId,
                ActorDiscordUserId = actorId,
                ActorDisplayName = actorDisplayName,
                ActionType = actionType,
                Detail = detail,
            },
            cancellationToken: cancellationToken));
    }
}
