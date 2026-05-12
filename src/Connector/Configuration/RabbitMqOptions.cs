using System.ComponentModel.DataAnnotations;

namespace Connector;

/// <summary>
/// Configuration options for RabbitMQ connection and consumer settings.
/// Validated at startup to catch configuration errors early.
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>
    /// Hostname or IP address of the RabbitMQ broker.
    /// </summary>
    [Required(ErrorMessage = "RabbitMQ Host is required.")]
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// Port number for RabbitMQ connection (5672 for AMQP, 5671 for AMQPS).
    /// </summary>
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    public int Port { get; set; } = 5672;

    /// <summary>
    /// RabbitMQ virtual host for logical separation.
    /// </summary>
    [Required(ErrorMessage = "RabbitMQ VirtualHost is required.")]
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Username for RabbitMQ authentication.
    /// </summary>
    [Required(ErrorMessage = "RabbitMQ Username is required.")]
    public string Username { get; set; } = "guest";

    /// <summary>
    /// Password for RabbitMQ authentication.
    /// Consider storing in local.settings.json or Azure Key Vault per environment.
    /// </summary>
    [Required(ErrorMessage = "RabbitMQ Password is required.")]
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Name of the inbound queue to consume from.
    /// </summary>
    [Required(ErrorMessage = "RabbitMQ QueueName is required.")]
    public string QueueName { get; set; } = "connector.inbound";
}
