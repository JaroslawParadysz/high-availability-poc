using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Events;

namespace Connector;

/// <summary>
/// Background worker that consumes messages from RabbitMQ.
/// Delegates connection management to IRabbitMqConnectionProvider.
/// Delegates business logic to IMessageHandler.
/// Each message is acknowledged only after successful processing (at-least-once delivery).
/// </summary>
public sealed class RabbitMqConsumerWorker : BackgroundService
{
    private readonly ILogger<RabbitMqConsumerWorker> _logger;
    private readonly IRabbitMqConnectionProvider _connectionProvider;
    private readonly IMessageHandler _messageHandler;
    private readonly RabbitMqOptions _options;

    public RabbitMqConsumerWorker(
        ILogger<RabbitMqConsumerWorker> logger,
        IRabbitMqConnectionProvider connectionProvider,
        IMessageHandler messageHandler,
        IOptions<RabbitMqOptions> options)
    {
        _logger = logger;
        _connectionProvider = connectionProvider;
        _messageHandler = messageHandler;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Connector starting. Queue={Queue}", _options.QueueName);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var channel = await _connectionProvider.GetChannelAsync(stoppingToken);
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += (sender, ea) =>
                        OnMessageReceivedAsync(channel, ea, stoppingToken);

                    await channel.BasicConsumeAsync(
                        queue: _options.QueueName,
                        autoAck: false,
                        consumerTag: "",
                        noLocal: false,
                        exclusive: false,
                        arguments: null,
                        consumer: consumer,
                        cancellationToken: stoppingToken);

                    _logger.LogInformation(
                        "Consumer started. Queue={Queue}",
                        _options.QueueName);

                    // Keep consumer alive until cancellation or unexpected error.
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Connector cancellation requested.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Consumer loop failed. Retrying in 5 seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Connector stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connector execution failed.");
            throw;
        }
    }

    internal async Task OnMessageReceivedAsync(
        RabbitMQ.Client.IChannel channel,
        BasicDeliverEventArgs ea,
        CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(ea.BasicProperties?.CorrelationId))
        {
            _logger.LogWarning(
                "Message rejected: missing correlation ID. DeliveryTag={DeliveryTag}",
                ea.DeliveryTag);
            // Nack with requeue: false so the broker routes the message to the dead-letter exchange.
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        var correlationId = ea.BasicProperties.CorrelationId;
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["DeliveryTag"] = ea.DeliveryTag,
        });

        try
        {
            var body = Encoding.UTF8.GetString(ea.Body.Span);
            _logger.LogInformation("Message received. Size={Size}", ea.Body.Length);

            // Delegate to business logic handler.
            await _messageHandler.HandleAsync(body, correlationId, stoppingToken);

            // Acknowledge only after successful processing.
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            _logger.LogInformation("Message acknowledged.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Message processing cancelled.");
            // Nack to requeue for later retries.
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message processing failed. Sending to dead-letter.");
            // Nack with requeue: false → routes to dead-letter exchange if configured.
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public override void Dispose()
    {
        _logger.LogInformation("Disposing consumer worker.");
        _connectionProvider.DisposeAsync().GetAwaiter().GetResult();
        base.Dispose();
    }
}
