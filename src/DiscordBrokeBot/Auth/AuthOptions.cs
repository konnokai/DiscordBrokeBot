namespace DiscordBrokeBot.Auth;

public sealed class AuthOptions
{
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
    public string PublicApiBaseUrl { get; set; } = "http://localhost:5000";
    public string CookieName { get; set; } = "chitu.auth";
    public int CookieHours { get; set; } = 4;
    public string CookieSameSite { get; set; } = "Lax";
}

public sealed class DiscordOptions
{
    public string BotToken { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

public sealed record DiscordUser(string Id, string DisplayName, string? AvatarUrl);
