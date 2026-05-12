namespace Connector;

/// <summary>
/// Placeholder message handler for the consume → persist → publish pipeline.
/// Replace with actual database writes (outbox pattern) and MQTT publish logic.
/// </summary>
public sealed class DefaultMessageHandler : IMessageHandler
{
    private readonly ILogger<DefaultMessageHandler> _logger;

    public DefaultMessageHandler(ILogger<DefaultMessageHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(string body, string correlationId, CancellationToken ct)
    {
        _logger.LogDebug(
            "Processing message. CorrelationId={CorrelationId} Body={Body}",
            correlationId, body);
        
        // TODO: Implement the full pipeline:
        // 1. Validate and parse message
        // 2. Write to database (with outbox pattern for MQTT publish)
        // 3. Publish to MQTT endpoint
        
        return Task.CompletedTask;
    }
}
