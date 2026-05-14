using Connector.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Connector.Infrastructure.Persistence.EntityConfiguration;

public class DuplicateEventConfiguration : IEntityTypeConfiguration<DuplicateEvent>
{
    public void Configure(EntityTypeBuilder<DuplicateEvent> builder)
    {
        builder.ToTable("duplicate_events");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("id");

        builder.Property(entity => entity.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired();

        builder.Property(entity => entity.ReceivedAt)
            .HasColumnName("received_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(entity => entity.SourceQueue)
            .HasColumnName("source_queue");

        builder.HasIndex(entity => entity.CorrelationId)
            .HasDatabaseName("ix_duplicate_events_correlation_id");
    }
}
