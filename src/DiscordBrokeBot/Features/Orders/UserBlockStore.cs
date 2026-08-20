using Dapper;
using DiscordBrokeBot.Features.Orders.Models;
using DiscordBrokeBot.Infrastructure.Database;

namespace DiscordBrokeBot.Features.Orders;

/// <summary>Persists the buyer-scoped requester block list.</summary>
/// <remarks>The composite UID key makes the block effective across every source Guild.</remarks>
public sealed class UserBlockStore(MySqlConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<UserBlockRow>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserBlockRow>(new CommandDefinition(
            """
            SELECT
                buyer_discord_user_id AS BuyerDiscordUserId,
                requester_discord_user_id AS RequesterDiscordUserId,
                requester_display_name AS RequesterDisplayName,
                created_at AS CreatedAt
            FROM user_blocks
            WHERE buyer_discord_user_id = @BuyerId
            ORDER BY created_at DESC, requester_discord_user_id;
            """,
            new { BuyerId = buyerId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<bool> AddAsync(
        string buyerId,
        string requesterId,
        string requesterDisplayName,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT IGNORE INTO user_blocks (
                buyer_discord_user_id,
                requester_discord_user_id,
                requester_display_name,
                created_at
            ) VALUES (@BuyerId, @RequesterId, @RequesterDisplayName, UTC_TIMESTAMP(6));
            """,
            new { BuyerId = buyerId, RequesterId = requesterId, RequesterDisplayName = requesterDisplayName },
            cancellationToken: cancellationToken));
        return affected == 1;
    }

    public async Task<bool> RemoveAsync(
        string buyerId,
        string requesterId,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM user_blocks
            WHERE buyer_discord_user_id = @BuyerId
              AND requester_discord_user_id = @RequesterId;
            """,
            new { BuyerId = buyerId, RequesterId = requesterId },
            cancellationToken: cancellationToken));
        return affected == 1;
    }
}
