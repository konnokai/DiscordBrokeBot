using DbUp;

namespace DiscordBrokeBot.Infrastructure.Database;

/// <summary>Applies embedded DbUp SQL before the application accepts requests.</summary>
public sealed class DbUpMigrationHostedService(
    MySqlConnectionFactory connectionFactory,
    ILogger<DbUpMigrationHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionString = connectionFactory.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("ConnectionStrings:Default is empty; database migrations are skipped.");
            return Task.CompletedTask;
        }

        var result = DeployChanges
            .To.MySqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationHostedService).Assembly)
            .LogToConsole()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            logger.LogError(result.Error, "Database migration failed.");
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        logger.LogInformation("Database migrations are up to date.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
