using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Connector.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
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

            options
                .UseNpgsql(dataSource, npgsqlOptions =>
                {
                    npgsqlOptions.CommandTimeout(persistenceOptions.CommandTimeoutSeconds);
                    npgsqlOptions.EnableRetryOnFailure(maxRetryCount: persistenceOptions.MaxRetryCount);
                })
                .UseSnakeCaseNamingConvention();
        });

        return services;
    }
}
