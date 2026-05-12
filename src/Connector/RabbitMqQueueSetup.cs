using Microsoft.Extensions.Options;

namespace Connector;

/// <summary>
/// Sets up RabbitMQ queue infrastructure at startup.
/// Ensures queues, exchanges, and dead-letter bindings exist before consumer starts.
/// </summary>
public sealed class RabbitMqQueueSetup : IRabbitMqQueueSetup
{
    private readonly ILogger<RabbitMqQueueSetup> _logger;
    private readonly IRabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;

    public RabbitMqQueueSetup(
        ILogger<RabbitMqQueueSetup> logger,
        IRabbitMqConnectionProvider connectionProvider,
        IOptions<RabbitMqOptions> options)
    {
        _logger = logger;
        _connectionProvider = connectionProvider;
        _options = options.Value;
    }

    public async Task SetupAsync(CancellationToken ct)
    {
        try
        {
            var channel = await _connectionProvider.GetChannelAsync(ct);

            // Declare queue with dead-letter exchange for failed messages.
            await channel.QueueDeclareAsync(
                queue: _options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = $"{_options.QueueName}.dlx",
                },
                cancellationToken: ct);

            // Declare dead-letter exchange.
            await channel.ExchangeDeclareAsync(
                exchange: $"{_options.QueueName}.dlx",
                type: "direct",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            // Declare dead-letter queue.
            await channel.QueueDeclareAsync(
                queue: $"{_options.QueueName}.dlq",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            // Bind dead-letter queue to dead-letter exchange.
            await channel.QueueBindAsync(
                queue: $"{_options.QueueName}.dlq",
                exchange: $"{_options.QueueName}.dlx",
                routingKey: _options.QueueName,
                arguments: null,
                cancellationToken: ct);

            _logger.LogInformation(
                "RabbitMQ queue setup complete. Queue={Queue} DLQ={DLQ}",
                _options.QueueName, $"{_options.QueueName}.dlq");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set up RabbitMQ queue infrastructure.");
            throw;
        }
    }
}
