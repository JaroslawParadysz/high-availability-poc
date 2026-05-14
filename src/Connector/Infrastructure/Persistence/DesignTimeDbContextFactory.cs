using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Connector.Infrastructure.Persistence;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ConnectorDbContext>
{
    public ConnectorDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString) ||
            string.Equals(connectionString, "MISSING_CONFIG", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Postgres must be set to a valid connection string for design-time DbContext creation. " +
                "Replace placeholder values in appsettings.json or set ConnectionStrings__Postgres environment variable.");
        }

        var persistenceOptions = configuration.GetSection("Persistence").Get<PersistenceOptions>()
            ?? throw new InvalidOperationException("Persistence section is required for design-time DbContext creation.");
        var commandTimeoutSeconds = persistenceOptions.CommandTimeoutSeconds
            ?? throw new InvalidOperationException("Persistence:CommandTimeoutSeconds is required for design-time DbContext creation.");
        var maxRetryCount = persistenceOptions.MaxRetryCount
            ?? throw new InvalidOperationException("Persistence:MaxRetryCount is required for design-time DbContext creation.");

        var optionsBuilder = new DbContextOptionsBuilder<ConnectorDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.CommandTimeout(commandTimeoutSeconds);
                npgsqlOptions.EnableRetryOnFailure(maxRetryCount: maxRetryCount);
            })
            .UseSnakeCaseNamingConvention();

        return new ConnectorDbContext(optionsBuilder.Options);
    }
}
