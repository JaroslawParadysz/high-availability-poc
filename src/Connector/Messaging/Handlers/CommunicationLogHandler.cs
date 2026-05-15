using Connector.Domain.Entities;
using Connector.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Connector;

/// <summary>
/// Persists every consumed message to <c>communication_log</c> with full idempotency and duplicate tracking.
/// </summary>
/// <remarks>
/// Happy path: inserts a row with <c>status = 'processed'</c>.
/// Duplicate (same <c>correlation_id</c>): inserts into <c>duplicate_events</c> within the same transaction.
/// Failure: inserts a row with <c>status = 'failed'</c> in a fresh scope, then rethrows.
/// Transient Npgsql errors: wrapped as <see cref="TransientPersistenceException"/> for NACK-with-requeue handling.
/// </remarks>
public sealed class CommunicationLogHandler : IMessageHandler
{
    private readonly ILogger<CommunicationLogHandler> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _sourceQueue;

    public CommunicationLogHandler(
        ILogger<CommunicationLogHandler> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> rabbitMqOptions)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _sourceQueue = rabbitMqOptions.Value.QueueName;
    }

    public async Task HandleAsync(string body, string correlationId, CancellationToken ct)
    {
        var correlationGuid = Guid.Parse(correlationId);
        var handledAt = DateTime.UtcNow;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();

            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

            const string processedStatus = "processed";
            var rowsAffected = await dbContext.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO communication_log (correlation_id, message_body, handled_at, status, source_queue)
                VALUES ({correlationGuid}, {body}, {handledAt}, {processedStatus}, {_sourceQueue})
                ON CONFLICT (correlation_id) DO NOTHING
                """, ct);

            if (rowsAffected == 0)
            {
                _logger.LogDebug(
                    "Duplicate message detected. CorrelationId={CorrelationId}", correlationId);

                dbContext.DuplicateEvents.Add(new DuplicateEvent
                {
                    CorrelationId = correlationGuid,
                    SourceQueue = _sourceQueue
                });
                await dbContext.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);

            _logger.LogDebug(
                "Message persisted. CorrelationId={CorrelationId} Status={Status}",
                correlationId, rowsAffected == 0 ? "duplicate" : processedStatus);
        }
        catch (NpgsqlException ex) when (ex.IsTransient)
        {
            throw new TransientPersistenceException(
                $"Transient persistence failure. CorrelationId={correlationId}", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await PersistFailureAsync(body, correlationGuid, handledAt, ex);
            throw;
        }
    }

    private async Task PersistFailureAsync(string body, Guid correlationGuid, DateTime handledAt, Exception ex)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();

            dbContext.CommunicationLogs.Add(new CommunicationLog
            {
                CorrelationId = correlationGuid,
                MessageBody = body,
                HandledAt = handledAt,
                Status = "failed",
                ErrorMessage = ex.Message,
                SourceQueue = _sourceQueue
            });

            // Use CancellationToken.None so that the failure record is always persisted,
            // even when the application is shutting down.
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception persistEx)
        {
            _logger.LogError(
                persistEx,
                "Failed to persist failure record. CorrelationId={CorrelationId}",
                correlationGuid);
        }
    }
}
