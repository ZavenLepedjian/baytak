using Azure.Core;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Baytak.Api.Data;

/// <summary>
/// The same model on PostgreSQL. It is its own context type so it carries its own migrations (Migrations/Postgres):
/// the SQL Server migrations name SQL Server column types. Code keeps injecting <see cref="BaytakDbContext"/>.
/// </summary>
public class PostgresDbContext : BaytakDbContext
{
    public PostgresDbContext(DbContextOptions<PostgresDbContext> options) : base(options) { }
}

public static class Postgres
{
    /// <summary>Scope of Microsoft Entra tokens for Azure Database for PostgreSQL.</summary>
    private const string TokenScope = "https://ossrdbms-aad.database.windows.net/.default";

    public static bool IsProvider(string provider) =>
        provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("Npgsql", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A data source for the connection string. With Database:EntraAuth=true the password is a Microsoft Entra token of the
    /// app's managed identity, refreshed before it expires, so no database password exists anywhere.
    /// </summary>
    public static NpgsqlDataSource CreateDataSource(string connection, IConfiguration config)
    {
        var builder = new NpgsqlDataSourceBuilder(connection);
        if (config.GetValue("Database:EntraAuth", false))
        {
            var clientId = config["Database:ManagedIdentityClientId"];
            TokenCredential credential = string.IsNullOrWhiteSpace(clientId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId });
            builder.UsePeriodicPasswordProvider(
                async (_, ct) => (await credential.GetTokenAsync(new TokenRequestContext([TokenScope]), ct)).Token,
                TimeSpan.FromMinutes(50), TimeSpan.FromSeconds(10));
        }
        return builder.Build();
    }
}

/// <summary>Lets `dotnet ef migrations add --context PostgresDbContext` build the model. No database is contacted.</summary>
public class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresDbContext>
{
    public PostgresDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresDbContext>().UseNpgsql("Host=localhost;Database=baytak").Options);
}
