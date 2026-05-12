namespace Connector;

/// <summary>
/// Handles business logic for processing messages consumed from RabbitMQ.
/// Separates message handling from infrastructure concerns for improved testability.
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// Processes a message from RabbitMQ.
    /// Implementers should include the full consume → persist → publish pipeline.
    /// </summary>
    /// <param name="body">The message body as a UTF-8 string.</param>
    /// <param name="correlationId">The correlation ID for tracing the message through the system.</param>
    /// <param name="ct">Cancellation token for graceful shutdown.</param>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    Task HandleAsync(string body, string correlationId, CancellationToken ct);
}
