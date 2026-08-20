using MySqlConnector;

namespace DiscordBrokeBot.Infrastructure.Database;

public sealed class MySqlConnectionFactory(IConfiguration configuration)
{
    public string? ConnectionString => configuration.GetConnectionString("Default");

    public MySqlConnection Create()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        return new MySqlConnection(ConnectionString);
    }
}
