using Connector.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Connector.Infrastructure.Persistence;

public class ConnectorDbContext(DbContextOptions<ConnectorDbContext> options) : DbContext(options)
{
    public DbSet<CommunicationLog> CommunicationLogs => Set<CommunicationLog>();
    public DbSet<DuplicateEvent> DuplicateEvents => Set<DuplicateEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConnectorDbContext).Assembly);
    }
}
