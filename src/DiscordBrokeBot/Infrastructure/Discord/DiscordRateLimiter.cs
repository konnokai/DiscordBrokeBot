using System.Threading.RateLimiting;

namespace DiscordBrokeBot.Infrastructure.Discord;

/// <summary>Applies the first-version in-memory Discord per-user operation limits.</summary>
public sealed class DiscordRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter =
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(
                key,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = key.StartsWith("add:", StringComparison.Ordinal)
                        ? 5
                        : 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

    public async ValueTask<bool> AllowAsync(
        string actorId,
        string operation,
        CancellationToken cancellationToken = default)
    {
        using var lease = await limiter.AcquireAsync($"{operation}:{actorId}", 1, cancellationToken);
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
