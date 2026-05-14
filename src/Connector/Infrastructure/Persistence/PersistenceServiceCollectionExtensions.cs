using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Connector.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PersistenceOptions>(configuration.GetSection("Persistence"));
        services.AddOptions<PersistenceOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(_ =>
        {
            var connectionString = configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            return dataSourceBuilder.Build();
        });

        services.AddDbContext<ConnectorDbContext>((serviceProvider, options) =>
        {
            var dataSource = serviceProvider.GetRequiredService<NpgsqlDataSource>();
            var persistenceOptions = serviceProvider.GetRequiredService<IOptions<PersistenceOptions>>().Value;
            var commandTimeoutSeconds = persistenceOptions.CommandTimeoutSeconds
                ?? throw new InvalidOperationException("Persistence:CommandTimeoutSeconds is not configured.");
            var maxRetryCount = persistenceOptions.MaxRetryCount
                ?? throw new InvalidOperationException("Persistence:MaxRetryCount is not configured.");

            options
                .UseNpgsql(dataSource, npgsqlOptions =>
                {
                    npgsqlOptions.CommandTimeout(commandTimeoutSeconds);
                    npgsqlOptions.EnableRetryOnFailure(maxRetryCount: maxRetryCount);
                })
                .UseSnakeCaseNamingConvention();
        });

        return services;
    }
}
