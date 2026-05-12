namespace Connector;

/// <summary>
/// Hosted service that runs RabbitMQ queue setup once at application startup.
/// Ensures all required queues, exchanges, and dead-letter bindings are created
/// before the consumer starts consuming messages.
/// </summary>
public sealed class RabbitMqQueueSetupService : IHostedService
{
    private readonly ILogger<RabbitMqQueueSetupService> _logger;
    private readonly IRabbitMqQueueSetup _queueSetup;

    public RabbitMqQueueSetupService(
        ILogger<RabbitMqQueueSetupService> logger,
        IRabbitMqQueueSetup queueSetup)
    {
        _logger = logger;
        _queueSetup = queueSetup;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting RabbitMQ queue setup...");
            await _queueSetup.SetupAsync(cancellationToken);
            _logger.LogInformation("RabbitMQ queue setup completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ queue setup failed. The application will not be able to consume messages.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
