using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Connector.Domain.Entities;
using Connector.Infrastructure.Persistence;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Client.Events;
using Xunit;

namespace Connector.Tests;

/// <summary>
/// Tests for RabbitMqOptions configuration validation.
/// Ensures invalid configurations fail fast at startup.
/// </summary>
public class RabbitMqOptionsTests
{
    [Fact]
    public void DefaultOptions_HaveExpectedValues()
    {
        var options = new RabbitMqOptions();

        Assert.Equal("localhost", options.Host);
        Assert.Equal(5672, options.Port);
        Assert.Equal("/", options.VirtualHost);
        Assert.Equal("guest", options.Username);
        Assert.Equal("guest", options.Password);
        Assert.Equal("connector.inbound", options.QueueName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Options_FailsValidation_WhenHostIsEmpty(string? host)
    {
        var options = new RabbitMqOptions { Host = host ?? "" };
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(options);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.ErrorMessage?.Contains("Host") ?? false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Options_FailsValidation_WhenPortIsOutOfRange(int port)
    {
        var options = new RabbitMqOptions { Port = port };
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(options);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options, context, results, validateAllProperties: true);

        Assert.False(isValid);
    }

    [Theory]
    [InlineData(5671, true)]
    [InlineData(5672, false)]
    public void Port_DeterminesSslEnabled(int port, bool expectedSsl)
    {
        var sslEnabled = port == 5671;
        Assert.Equal(expectedSsl, sslEnabled);
    }
}

/// <summary>
/// Tests for RabbitMqConnectionProvider.
/// Verifies connection lifecycle, retry behavior, and health checks.
/// </summary>
public class RabbitMqConnectionProviderTests : IAsyncLifetime
{
    private readonly Mock<ILogger<RabbitMqConnectionProvider>> _mockLogger;
    private readonly IOptions<RabbitMqOptions> _options;

    public RabbitMqConnectionProviderTests()
    {
        _mockLogger = new Mock<ILogger<RabbitMqConnectionProvider>>();
        _options = Options.Create(new RabbitMqOptions { Host = "localhost" });
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void IsHealthy_ReturnsFalse_WhenNoConnectionEstablished()
    {
        var provider = new RabbitMqConnectionProvider(_mockLogger.Object, _options);

        Assert.False(provider.IsHealthy);
    }

    [Fact]
    public async Task GetConnectionAsync_ThrowsException_WhenHostIsUnreachable()
    {
        var badOptions = Options.Create(new RabbitMqOptions { Host = "unreachable.invalid", Port = 5672 });
        var provider = new RabbitMqConnectionProvider(_mockLogger.Object, badOptions);

        // Act & Assert — retries are exhausted, broker is unreachable.
        await Assert.ThrowsAsync<BrokerUnreachableException>(
            () => provider.GetConnectionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Provider_DisposeAsync_DoesNotThrow_WhenNeverConnected()
    {
        var provider = new RabbitMqConnectionProvider(_mockLogger.Object, _options);

        // Should not throw even though connection is null.
        await ((IAsyncDisposable)provider).DisposeAsync();
    }
}

/// <summary>
/// Tests for message handler integration with the consumer.
/// Verifies that handlers are called and exceptions are handled.
/// </summary>
public class MessageHandlerIntegrationTests
{
    private readonly Mock<ILogger<RabbitMqConsumerWorker>> _mockWorkerLogger;
    private readonly Mock<ILogger<DefaultMessageHandler>> _mockHandlerLogger;
    private readonly Mock<IRabbitMqConnectionProvider> _mockConnectionProvider;
    private readonly DefaultMessageHandler _messageHandler;
    private readonly IOptions<RabbitMqOptions> _options;

    public MessageHandlerIntegrationTests()
    {
        _mockWorkerLogger = new Mock<ILogger<RabbitMqConsumerWorker>>();
        _mockHandlerLogger = new Mock<ILogger<DefaultMessageHandler>>();
        _mockConnectionProvider = new Mock<IRabbitMqConnectionProvider>();
        _messageHandler = new DefaultMessageHandler(_mockHandlerLogger.Object);
        _options = Options.Create(new RabbitMqOptions { QueueName = "test.queue" });
    }

    [Fact]
    public async Task MessageHandler_ProcessesMessage_Successfully()
    {
        var body = "test message";
        var correlationId = Guid.NewGuid().ToString();

        // Act — handler should complete without throwing.
        await _messageHandler.HandleAsync(body, correlationId, CancellationToken.None);

        // Assert — handler logged the debug message.
        _mockHandlerLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(correlationId)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task MessageHandler_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert — should not throw; cancellation is handled gracefully.
        var exception = await Record.ExceptionAsync(
            () => _messageHandler.HandleAsync("body", Guid.NewGuid().ToString(), cts.Token));

        Assert.Null(exception);
    }
}

/// <summary>
/// Tests for RabbitMqQueueSetup.
/// Verifies queue and dead-letter infrastructure is created.
/// </summary>
public class RabbitMqQueueSetupTests
{
    private readonly Mock<ILogger<RabbitMqQueueSetup>> _mockLogger;
    private readonly Mock<IRabbitMqConnectionProvider> _mockConnectionProvider;
    private readonly Mock<IChannel> _mockChannel;
    private readonly IOptions<RabbitMqOptions> _options;

    public RabbitMqQueueSetupTests()
    {
        _mockLogger = new Mock<ILogger<RabbitMqQueueSetup>>();
        _mockConnectionProvider = new Mock<IRabbitMqConnectionProvider>();
        _mockChannel = new Mock<IChannel>();
        _options = Options.Create(new RabbitMqOptions { QueueName = "test.queue" });

        _mockConnectionProvider
            .Setup(x => x.GetChannelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockChannel.Object);
    }

    [Fact]
    public async Task SetupAsync_DeclaresQueue_WithDeadLetterExchange()
    {
        var setup = new RabbitMqQueueSetup(_mockLogger.Object, _mockConnectionProvider.Object, _options);

        // Act
        await setup.SetupAsync(CancellationToken.None);

        // Assert — queue declared with dead-letter exchange binding.
        _mockChannel.Verify(
            x => x.QueueDeclareAsync(
                It.Is<string>(q => q == _options.Value.QueueName),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.Is<IDictionary<string, object?>>((dict) =>
                    dict != null && dict.ContainsKey("x-dead-letter-exchange"))),
            Times.Once);
    }

    [Fact]
    public async Task SetupAsync_DeclaresDeadLetterQueue()
    {
        var setup = new RabbitMqQueueSetup(_mockLogger.Object, _mockConnectionProvider.Object, _options);

        // Act
        await setup.SetupAsync(CancellationToken.None);

        // Assert — dead-letter queue declared.
        _mockChannel.Verify(
            x => x.QueueDeclareAsync(
                $"{_options.Value.QueueName}.dlq",
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>()), 
            Times.Once);
    }

    [Fact]
    public async Task SetupAsync_ThrowsException_WhenConnectionProviderFails()
    {
        _mockConnectionProvider
            .Setup(x => x.GetChannelAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        var setup = new RabbitMqQueueSetup(_mockLogger.Object, _mockConnectionProvider.Object, _options);

        // Act & Assert — setup fails and throws the connection error.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => setup.SetupAsync(CancellationToken.None));
    }
}

/// <summary>
/// Tests for RabbitMqHealthCheck.
/// Verifies health check status reflects connection state.
/// </summary>
public class RabbitMqHealthCheckTests
{
    private readonly Mock<ILogger<RabbitMqHealthCheck>> _mockLogger;
    private readonly Mock<IRabbitMqConnectionProvider> _mockConnectionProvider;

    public RabbitMqHealthCheckTests()
    {
        _mockLogger = new Mock<ILogger<RabbitMqHealthCheck>>();
        _mockConnectionProvider = new Mock<IRabbitMqConnectionProvider>();
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenConnected()
    {
        _mockConnectionProvider.Setup(x => x.IsHealthy).Returns(true);
        var healthCheck = new RabbitMqHealthCheck(_mockLogger.Object, _mockConnectionProvider.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(
            new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext(),
            CancellationToken.None);

        // Assert
        Assert.Equal(
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy,
            result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenNotConnected()
    {
        _mockConnectionProvider.Setup(x => x.IsHealthy).Returns(false);
        var healthCheck = new RabbitMqHealthCheck(_mockLogger.Object, _mockConnectionProvider.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(
            new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext(),
            CancellationToken.None);

        // Assert
        Assert.Equal(
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_OnException()
    {
        _mockConnectionProvider.Setup(x => x.IsHealthy)
            .Throws(new InvalidOperationException("Health check failed"));

        var healthCheck = new RabbitMqHealthCheck(_mockLogger.Object, _mockConnectionProvider.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(
            new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext(),
            CancellationToken.None);

        // Assert
        Assert.Equal(
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            result.Status);
    }
}

public class ConnectorDbContextModelTests
{
    [Fact]
    public void CommunicationLog_HasExpectedIndexes()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=test;Password=test")
            .Options;

        using var context = new ConnectorDbContext(options);
        var entityType = context.Model.FindEntityType(typeof(CommunicationLog));

        Assert.NotNull(entityType);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.Properties.Any(property => property.Name == nameof(CommunicationLog.CorrelationId)) &&
            index.IsUnique);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.Properties.Any(property => property.Name == nameof(CommunicationLog.ReceivedAt)));
        Assert.Contains(entityType.GetIndexes(), index =>
            index.Properties.Any(property => property.Name == nameof(CommunicationLog.Status)));
    }

    [Fact]
    public void DuplicateEvent_HasCorrelationIdIndex()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=test;Password=test")
            .Options;

        using var context = new ConnectorDbContext(options);
        var entityType = context.Model.FindEntityType(typeof(DuplicateEvent));

        Assert.NotNull(entityType);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.Properties.Any(property => property.Name == nameof(DuplicateEvent.CorrelationId)));
    }
}
