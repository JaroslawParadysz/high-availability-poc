using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Connector;

/// <summary>
/// Health check for RabbitMQ connection status.
/// Reports Healthy when the connection is open and ready to consume messages.
/// </summary>
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly ILogger<RabbitMqHealthCheck> _logger;
    private readonly IRabbitMqConnectionProvider _connectionProvider;

    public RabbitMqHealthCheck(
        ILogger<RabbitMqHealthCheck> logger,
        IRabbitMqConnectionProvider connectionProvider)
    {
        _logger = logger;
        _connectionProvider = connectionProvider;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        try
        {
            if (_connectionProvider.IsHealthy)
            {
                return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ connection is open."));
            }

            _logger.LogWarning("RabbitMQ health check: connection is closed.");
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ connection is closed."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ health check failed.");
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ health check failed.", ex));
        }
    }
}
