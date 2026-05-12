using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client;

namespace Connector;

/// <summary>
/// Implements IRabbitMqConnectionProvider with exponential backoff retry policy.
/// Manages connection lifecycle and handles reconnection on failure.
/// </summary>
public sealed class RabbitMqConnectionProvider : IRabbitMqConnectionProvider
{
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly RabbitMqOptions _options;
    private readonly IAsyncPolicy _retryPolicy;
    
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqConnectionProvider(
        ILogger<RabbitMqConnectionProvider> logger,
        IOptions<RabbitMqOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        
        // Build exponential backoff retry policy: 5 retries, max 30s delay.
        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))),
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex,
                        "RabbitMQ connection retry attempt {Attempt} in {Delay}s.",
                        attempt, delay.TotalSeconds));
    }

    public bool IsHealthy => _connection is not null && _connection.IsOpen;

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is not null && _connection.IsOpen)
            return _connection;

        await _retryPolicy.ExecuteAsync(async () =>
        {
            await ConnectAsync(ct);
        });

        return _connection!;
    }

    public async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        var connection = await GetConnectionAsync(ct);
        
        if (_channel is not null && _channel.IsOpen)
            return _channel;

        _channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, ct);

        return _channel;
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                VirtualHost = _options.VirtualHost,
                UserName = _options.Username,
                Password = _options.Password,
                Ssl = new SslOption
                {
                    Enabled = _options.Port == 5671,
                    ServerName = _options.Host,
                },
            };

            _connection = await factory.CreateConnectionAsync(ct);
            _logger.LogInformation(
                "Connected to RabbitMQ. Host={Host} VirtualHost={VirtualHost}",
                _options.Host, _options.VirtualHost);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ.");
            throw;
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing RabbitMQ channel.");
            }
            finally
            {
                _channel.Dispose();
            }
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing RabbitMQ connection.");
            }
            finally
            {
                _connection.Dispose();
            }
        }
    }
}
