namespace Connector;

/// <summary>
/// Initializes RabbitMQ queue infrastructure (declaration, bindings, etc.).
/// Separated from the consumer worker to run once at startup.
/// </summary>
public interface IRabbitMqQueueSetup
{
    /// <summary>
    /// Sets up the required queue, bindings, and exchanges.
    /// Safe to call multiple times; idempotent.
    /// </summary>
    Task SetupAsync(CancellationToken ct);
}
