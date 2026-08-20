using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace DiscordBrokeBot.Auth;

/// <summary>Runs Discord Authorization Code + PKCE without persisting an access token.</summary>
/// <remarks>
/// The protected state contains the short-lived verifier and target URL. It is tamper resistant
/// and expires quickly, while the resulting ASP.NET Core cookie remains the only browser session.
/// </remarks>
public sealed class DiscordOAuthService(
    IHttpClientFactory httpClientFactory,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<AuthOptions> authOptions,
    IOptions<DiscordOptions> discordOptions)
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector stateProtector =
        dataProtectionProvider.CreateProtector("DiscordBrokeBot.OAuthState.v1");

    public string CreateLoginUrl()
    {
        var options = discordOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            throw new InvalidOperationException("Discord OAuth is not configured.");

        var verifier = CreateRandomString(32);
        var returnTo = $"{authOptions.Value.FrontendBaseUrl.TrimEnd('/')}/oauth/callback";
        var state = stateProtector.Protect(JsonSerializer.Serialize(new OAuthState(
            verifier,
            DateTimeOffset.UtcNow,
            returnTo)));
        var challenge = Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        var redirectUri = GetRedirectUri();
        return QueryHelpers.AddQueryString(
            "https://discord.com/oauth2/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = options.ClientId,
                ["response_type"] = "code",
                ["redirect_uri"] = redirectUri,
                ["scope"] = "identify",
                ["state"] = state,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
            });
    }

    public async Task<(DiscordUser User, string ReturnTo)> CompleteAsync(
        string code,
        string state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            throw new InvalidOperationException("Discord OAuth callback is missing code or state.");

        OAuthState oauthState;
        try
        {
            oauthState = JsonSerializer.Deserialize<OAuthState>(stateProtector.Unprotect(state))
                ?? throw new InvalidOperationException("OAuth state is invalid.");
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new InvalidOperationException("OAuth state is invalid or expired.", exception);
        }

        if (DateTimeOffset.UtcNow - oauthState.IssuedAt > StateLifetime)
            throw new InvalidOperationException("OAuth state is expired.");

        var options = discordOptions.Value;
        var client = httpClientFactory.CreateClient("discord");
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = GetRedirectUri(),
                ["code_verifier"] = oauthState.CodeVerifier,
            }),
        };
        using var tokenResponse = await client.SendAsync(tokenRequest, cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
            throw new InvalidOperationException("Discord OAuth token exchange failed.");
        var token = await tokenResponse.Content.ReadFromJsonAsync<DiscordTokenResponse>(cancellationToken);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
            throw new InvalidOperationException("Discord OAuth did not return an access token.");

        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/users/@me");
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var userResponse = await client.SendAsync(userRequest, cancellationToken);
        if (!userResponse.IsSuccessStatusCode)
            throw new InvalidOperationException("Discord user lookup failed.");
        var profile = await userResponse.Content.ReadFromJsonAsync<DiscordProfile>(cancellationToken)
            ?? throw new InvalidOperationException("Discord returned an empty user profile.");
        if (string.IsNullOrWhiteSpace(profile.Id))
            throw new InvalidOperationException("Discord user profile has no ID.");

        var displayName = profile.GlobalName ?? profile.Username ?? profile.Id;
        var avatarUrl = profile.Avatar is null
            ? null
            : $"https://cdn.discordapp.com/avatars/{profile.Id}/{profile.Avatar}.png?size=128";
        return (new DiscordUser(profile.Id, displayName, avatarUrl), NormalizeReturnTo(oauthState.ReturnTo));
    }

    public string GetRedirectUri() => $"{authOptions.Value.PublicApiBaseUrl.TrimEnd('/')}/auth/callback";

    public ClaimsPrincipal CreatePrincipal(DiscordUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new("discord_user_id", user.Id),
            new(ClaimTypes.Name, user.DisplayName),
        };
        if (user.AvatarUrl is not null)
            claims.Add(new Claim("discord_avatar_url", user.AvatarUrl));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Discord"));
    }

    private string NormalizeReturnTo(string value)
    {
        var configured = authOptions.Value.FrontendBaseUrl.TrimEnd('/');
        return value.StartsWith(configured, StringComparison.OrdinalIgnoreCase) ? value : configured;
    }

    private static string CreateRandomString(int byteCount) =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    private static string Base64UrlEncode(byte[] bytes) =>
        WebEncoders.Base64UrlEncode(bytes);

    private sealed record OAuthState(
        string CodeVerifier,
        DateTimeOffset IssuedAt,
        string ReturnTo);

    private sealed record DiscordTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record DiscordProfile(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("global_name")] string? GlobalName,
        [property: JsonPropertyName("avatar")] string? Avatar);
}
