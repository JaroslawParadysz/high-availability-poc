using Connector.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Connector.Infrastructure.Persistence.EntityConfiguration;

public class CommunicationLogConfiguration : IEntityTypeConfiguration<CommunicationLog>
{
    public void Configure(EntityTypeBuilder<CommunicationLog> builder)
    {
        builder.ToTable("communication_log");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.Id)
            .HasColumnName("id");

        builder.Property(entity => entity.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired();

        builder.Property(entity => entity.MessageBody)
            .HasColumnName("message_body")
            .IsRequired();

        builder.Property(entity => entity.ReceivedAt)
            .HasColumnName("received_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(entity => entity.HandledAt)
            .HasColumnName("handled_at")
            .IsRequired();

        builder.Property(entity => entity.Status)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(entity => entity.ErrorMessage)
            .HasColumnName("error_message");

        builder.Property(entity => entity.SourceQueue)
            .HasColumnName("source_queue");

        builder.HasIndex(entity => entity.CorrelationId)
            .HasDatabaseName("ix_communication_log_correlation_id")
            .IsUnique();

        builder.HasIndex(entity => entity.ReceivedAt)
            .HasDatabaseName("ix_communication_log_received_at");

        builder.HasIndex(entity => entity.Status)
            .HasDatabaseName("ix_communication_log_status");
    }
}
