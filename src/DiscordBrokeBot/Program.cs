using System.Net;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Dapper;
using DiscordBrokeBot.Api;
using DiscordBrokeBot.Auth;
using DiscordBrokeBot.Features.Orders;
using DiscordBrokeBot.Features.Orders.Models;
using DiscordBrokeBot.Infrastructure.Database;
using DiscordBrokeBot.Infrastructure.Discord;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using MySqlConnector;

DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.Configure<AuthOptions>(configuration.GetSection("Auth"));
builder.Services.Configure<DiscordOptions>(configuration.GetSection("Discord"));
builder.Services.AddHttpClient("discord", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBrokeBot/1.0");
});

var keyPath = configuration["DataProtection:KeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, ".keys");
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
    .SetApplicationName("DiscordBrokeBot");

var authOptions = configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
var secureCookies = !builder.Environment.IsDevelopment();
builder.Services
    .AddAuthentication("DiscordCookie")
    .AddCookie("DiscordCookie", options =>
    {
        options.Cookie.Name = authOptions.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = secureCookies
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = ParseSameSite(authOptions.CookieSameSite);
        options.ExpireTimeSpan = TimeSpan.FromHours(authOptions.CookieHours);
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var frontendOrigin = authOptions.FrontendBaseUrl.TrimEnd('/');
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
    policy.WithOrigins(frontendOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiError("操作太頻繁，請稍後再試。"),
            cancellationToken);
    };
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        GetRemoteAddress(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
    options.AddPolicy("api-query", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.DiscordUserId() ?? GetRemoteAddress(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
    options.AddPolicy("api-mutation", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.DiscordUserId() ?? GetRemoteAddress(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    var configuredProxies = configuration["ForwardedHeaders:KnownProxies"]?
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? [];
    foreach (var proxy in configuredProxies)
    {
        if (IPAddress.TryParse(proxy, out var address))
            options.KnownProxies.Add(address);
    }
});

builder.Services.AddOpenApi();
builder.Services.AddSingleton<MySqlConnectionFactory>();
builder.Services.AddSingleton<OrderStore>();
builder.Services.AddSingleton<PaymentEntryStore>();
builder.Services.AddSingleton<UserBlockStore>();
builder.Services.AddSingleton<OrderService>();
builder.Services.AddSingleton<DiscordOAuthService>();
builder.Services.AddSingleton<CsrfService>();
builder.Services.AddHostedService<DbUpMigrationHostedService>();
builder.Services.AddDiscordBot();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseCors("frontend");
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OrderNotFoundException exception)
    {
        await WriteErrorAsync(context, StatusCodes.Status404NotFound, exception.Message);
    }
    catch (OrderRuleException exception)
    {
        await WriteErrorAsync(context, StatusCodes.Status400BadRequest, exception.Message);
    }
    catch (MySqlException exception)
    {
        app.Logger.LogError(exception, "Database operation failed.");
        await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "資料庫目前無法使用，請稍後再試。");
    }
    catch (InvalidOperationException exception)
    {
        app.Logger.LogError(exception, "Application operation failed.");
        await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "服務目前尚未完成設定，請稍後再試。");
    }
});
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var isMutation = context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE";
    var isProtectedPath = context.Request.Path.StartsWithSegments("/api")
        || context.Request.Path.Equals("/auth/logout", StringComparison.OrdinalIgnoreCase);
    if (isMutation && isProtectedPath && context.User.Identity?.IsAuthenticated == true)
    {
        var actorId = context.User.DiscordUserId();
        var csrf = context.RequestServices.GetRequiredService<CsrfService>();
        if (actorId is null || !csrf.IsValid(actorId, context.Request.Headers["X-CSRF-Token"].FirstOrDefault()))
        {
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "CSRF token 無效或缺失。");
            return;
        }
    }
    await next();
});
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "DiscordBrokeBot", status = "ok" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
AuthEndpoints.Map(app);
ApiEndpoints.Map(app);
app.MapOpenApi();

app.Run();

static SameSiteMode ParseSameSite(string value) =>
    value.ToLowerInvariant() switch
    {
        "strict" => SameSiteMode.Strict,
        "none" => SameSiteMode.None,
        _ => SameSiteMode.Lax,
    };

static string GetRemoteAddress(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
{
    if (context.Response.HasStarted)
        return;
    context.Response.StatusCode = statusCode;
    await context.Response.WriteAsJsonAsync(new ApiError(message));
}

public partial class Program;
