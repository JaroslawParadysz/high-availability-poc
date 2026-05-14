using Microsoft.EntityFrameworkCore;
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

            options
                .UseNpgsql(dataSource)
                .UseSnakeCaseNamingConvention();
        });

        return services;
    }
}
