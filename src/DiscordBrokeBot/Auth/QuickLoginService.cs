using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace DiscordBrokeBot.Auth;

/// <summary>Creates short-lived, one-time links that exchange for the normal auth cookie.</summary>
public sealed class QuickLoginService(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<AuthOptions> authOptions)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector tokenProtector =
        dataProtectionProvider.CreateProtector("DiscordBrokeBot.QuickLogin.v1");
    // ponytail: process-local replay guard; use shared Redis before running multiple backend instances.
    private readonly ConcurrentDictionary<string, DateTimeOffset> consumedTokens = new();

    public string CreateUrl(string userId, string displayName)
    {
        var grant = new QuickLoginGrant(
            userId,
            displayName,
            DateTimeOffset.UtcNow,
            Guid.NewGuid().ToString("N"));
        var token = tokenProtector.Protect(JsonSerializer.Serialize(grant));
        return $"{authOptions.Value.FrontendBaseUrl.TrimEnd('/')}/quick-login#token={Uri.EscapeDataString(token)}";
    }

    public bool TryConsume(string token, out DiscordUser user)
    {
        user = null!;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        QuickLoginGrant grant;
        try
        {
            var parsed = JsonSerializer.Deserialize<QuickLoginGrant>(tokenProtector.Unprotect(token));
            if (parsed is null)
                return false;
            grant = parsed;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(grant.UserId)
            || string.IsNullOrWhiteSpace(grant.DisplayName)
            || string.IsNullOrWhiteSpace(grant.Nonce)
            || grant.IssuedAt > now
            || now - grant.IssuedAt > Lifetime)
            return false;

        foreach (var consumed in consumedTokens)
        {
            if (consumed.Value <= now)
                consumedTokens.TryRemove(consumed.Key, out _);
        }

        if (!consumedTokens.TryAdd(grant.Nonce, grant.IssuedAt.Add(Lifetime)))
            return false;

        user = new DiscordUser(grant.UserId, grant.DisplayName, null);
        return true;
    }

    private sealed record QuickLoginGrant(
        string UserId,
        string DisplayName,
        DateTimeOffset IssuedAt,
        string Nonce);
}
