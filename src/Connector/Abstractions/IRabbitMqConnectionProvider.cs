using RabbitMQ.Client;

namespace Connector;

/// <summary>
/// Provides connections and channels to RabbitMQ with built-in retry and reconnection logic.
/// Abstracts connection management from consumer logic for improved testability.
/// </summary>
public interface IRabbitMqConnectionProvider : IAsyncDisposable
{
    /// <summary>
    /// Gets or establishes a connection to RabbitMQ.
    /// Handles reconnection if the current connection is closed or lost.
    /// </summary>
    Task<IConnection> GetConnectionAsync(CancellationToken ct);

    /// <summary>
    /// Gets or establishes a channel on the current RabbitMQ connection.
    /// </summary>
    Task<IChannel> GetChannelAsync(CancellationToken ct);

    /// <summary>
    /// Gets the current health status of the connection.
    /// </summary>
    bool IsHealthy { get; }
}
