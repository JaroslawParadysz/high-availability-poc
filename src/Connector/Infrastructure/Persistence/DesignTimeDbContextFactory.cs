using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Connector.Infrastructure.Persistence;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ConnectorDbContext>
{
    public ConnectorDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ConnectorDbContext>();
        optionsBuilder
            .UseNpgsql("Host=localhost;Database=connector_dev;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        return new ConnectorDbContext(optionsBuilder.Options);
    }
}
