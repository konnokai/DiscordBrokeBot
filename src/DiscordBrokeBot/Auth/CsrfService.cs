using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace DiscordBrokeBot.Auth;

/// <summary>Creates short-lived, user-bound CSRF headers for state-changing requests.</summary>
public sealed class CsrfService(IDataProtectionProvider dataProtectionProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private readonly IDataProtector protector =
        dataProtectionProvider.CreateProtector("DiscordBrokeBot.Csrf.v1");

    public string CreateToken(string userId) => protector.Protect($"{userId}|{DateTimeOffset.UtcNow:O}|{Guid.NewGuid():N}");

    public bool IsValid(string userId, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;
        try
        {
            var parts = protector.Unprotect(token).Split('|', 3);
            return parts.Length == 3
                && CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(parts[0]),
                    Encoding.UTF8.GetBytes(userId))
                && DateTimeOffset.TryParse(parts[1], out var issuedAt)
                && DateTimeOffset.UtcNow - issuedAt <= Lifetime;
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or FormatException)
        {
            return false;
        }
    }
}

public static class ClaimsPrincipalExtensions
{
    public static string? DiscordUserId(this ClaimsPrincipal principal) =>
        principal.FindFirst("discord_user_id")?.Value ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
}
