using DiscordBrokeBot.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace DiscordBrokeBot.Api;

public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/auth/login", (DiscordOAuthService oauth) =>
            Results.Redirect(oauth.CreateLoginUrl()))
            .RequireRateLimiting("auth");

        app.MapGet("/auth/callback", async (
            string? code,
            string? state,
            HttpContext context,
            DiscordOAuthService oauth,
            IOptions<AuthOptions> authOptions,
            CancellationToken cancellationToken) =>
        {
            var result = await oauth.CompleteAsync(code ?? "", state ?? "", cancellationToken);
            await context.SignInAsync(
                "DiscordCookie",
                oauth.CreatePrincipal(result.User),
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(authOptions.Value.CookieHours),
                });
            return Results.Redirect(result.ReturnTo);
        }).RequireRateLimiting("auth");

        app.MapPost("/auth/logout", async (
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await context.SignOutAsync("DiscordCookie");
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("api-mutation");
    }
}
