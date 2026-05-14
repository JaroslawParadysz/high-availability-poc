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

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required for design-time DbContext creation.");

        var persistenceOptions = configuration.GetSection("Persistence").Get<PersistenceOptions>() ?? new PersistenceOptions();

        var optionsBuilder = new DbContextOptionsBuilder<ConnectorDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.CommandTimeout(persistenceOptions.CommandTimeoutSeconds);
                npgsqlOptions.EnableRetryOnFailure(maxRetryCount: persistenceOptions.MaxRetryCount);
            })
            .UseSnakeCaseNamingConvention();

        return new ConnectorDbContext(optionsBuilder.Options);
    }
}
