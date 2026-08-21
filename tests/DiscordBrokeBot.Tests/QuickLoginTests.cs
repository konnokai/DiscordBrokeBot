using DiscordBrokeBot.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace DiscordBrokeBot.Tests;

public sealed class QuickLoginTests
{
    [Fact]
    public void Quick_login_token_is_single_use()
    {
        var service = new QuickLoginService(
            new EphemeralDataProtectionProvider(),
            Options.Create(new AuthOptions { FrontendBaseUrl = "http://localhost:5173" }));
        var url = service.CreateUrl("123", "tester");
        var token = Uri.UnescapeDataString(new Uri(url).Fragment["#token=".Length..]);

        Assert.True(service.TryConsume(token, out var user));
        Assert.Equal("123", user.Id);
        Assert.Equal("tester", user.DisplayName);
        Assert.False(service.TryConsume(token, out _));
    }
}
