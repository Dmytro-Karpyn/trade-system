using Npgsql;

namespace Trade.Api.Operations;

public static class ProductionConfiguration
{
    public static void Validate(IHostEnvironment environment, IConfiguration configuration)
    {
        if (environment.IsDevelopment()) return;
        var raw = configuration.GetConnectionString("TradeDatabase");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("ConnectionStrings__TradeDatabase must be provided outside Development.");
        NpgsqlConnectionStringBuilder connection;
        try { connection = new(raw); }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        { throw new InvalidOperationException("ConnectionStrings__TradeDatabase is invalid; configuration values are not logged."); }
        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database) ||
            string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrWhiteSpace(connection.Password))
            throw new InvalidOperationException("The database host, database, username and password must be explicitly configured.");
        if (connection.Password is "trade_local_password" or "Test123!Test" ||
            connection.Password.StartsWith("replace-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A development or example database password cannot be used outside Development.");
        if (connection.IncludeErrorDetail || connection.LogParameters || connection.PersistSecurityInfo)
            throw new InvalidOperationException("Database error details, parameter logging and PersistSecurityInfo must be disabled outside Development.");
    }
}
