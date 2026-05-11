using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Connector;

/// <summary>
/// Background worker that consumes messages from RabbitMQ.
/// Each message is acknowledged only after successful processing (at-least-once delivery).
/// </summary>
public sealed class RabbitMqConsumerWorker : BackgroundService
{
    private readonly ILogger<RabbitMqConsumerWorker> _logger;
    private readonly RabbitMqOptions _options;
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqConsumerWorker(
        ILogger<RabbitMqConsumerWorker> logger,
        IOptions<RabbitMqOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Connector starting. Queue={Queue}", _options.QueueName);

        await ConnectWithRetryAsync(stoppingToken);

        // Keep alive until cancellation; reconnect on unexpected disconnect.
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_connection is null || !_connection.IsOpen)
            {
                _logger.LogWarning("RabbitMQ connection lost. Reconnecting…");
                await ConnectWithRetryAsync(stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            attempt++;
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = _options.Host,
                    Port = _options.Port,
                    VirtualHost = _options.VirtualHost,
                    UserName = _options.Username,
                    Password = _options.Password,
                    // Enforce TLS for all external connections.
                    Ssl = new SslOption
                    {
                        Enabled = _options.Port == 5671,
                        ServerName = _options.Host,
                    },
                };

                _connection = await factory.CreateConnectionAsync(ct);
                _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

                await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, ct);

                await _channel.QueueDeclarePassiveAsync(_options.QueueName, ct);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += OnMessageReceivedAsync;

                await _channel.BasicConsumeAsync(_options.QueueName, autoAck: false, consumer, ct);

                _logger.LogInformation(
                    "Connected to RabbitMQ. Host={Host} Queue={Queue}",
                    _options.Host, _options.QueueName);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                _logger.LogError(ex,
                    "Failed to connect to RabbitMQ (attempt {Attempt}). Retrying in {Delay}s.",
                    attempt, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var correlationId = ea.BasicProperties.CorrelationId ?? Guid.NewGuid().ToString();
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["DeliveryTag"] = ea.DeliveryTag,
        });

        try
        {
            var body = Encoding.UTF8.GetString(ea.Body.Span);
            _logger.LogInformation("Message received. Size={Size}", ea.Body.Length);

            await ProcessMessageAsync(body, correlationId);

            if (_channel is not null)
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);

            _logger.LogInformation("Message acknowledged.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message processing failed. Sending to dead-letter.");

            if (_channel is not null)
                // requeue: false → routes to dead-letter exchange if configured.
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    /// <summary>
    /// Placeholder for the full consume → persist → publish pipeline.
    /// Replace with actual DB write (outbox) and MQTT publish steps.
    /// </summary>
    private Task ProcessMessageAsync(string body, string correlationId)
    {
        _logger.LogDebug("Processing message. CorrelationId={CorrelationId} Body={Body}",
            correlationId, body);
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
