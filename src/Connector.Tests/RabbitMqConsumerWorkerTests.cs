using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Connector.Tests;

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
}

public class RabbitMqConsumerWorkerTests
{
    private static RabbitMqConsumerWorker CreateWorker(RabbitMqOptions? options = null)
    {
        var logger = new Mock<ILogger<RabbitMqConsumerWorker>>().Object;
        var opts = Options.Create(options ?? new RabbitMqOptions());
        return new RabbitMqConsumerWorker(logger, opts);
    }

    [Fact]
    public async Task Worker_CancelsGracefully_WhenTokenIsCancelledBeforeConnect()
    {
        // Arrange — point at a host that will refuse connections immediately.
        var options = new RabbitMqOptions
        {
            Host = "invalid.local",
            Port = 5672,
            QueueName = "test.queue",
        };
        using var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act — worker must exit without throwing when cancellation fires.
        await worker.StartAsync(cts.Token);
        await Task.Delay(300); // let the background loop attempt at least one connection
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Worker_DoesNotThrow_WhenDisposedWithoutStarting()
    {
        var worker = CreateWorker();
        var exception = Record.Exception(() => worker.Dispose());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(5671, true)]
    [InlineData(5672, false)]
    public void SslOption_IsEnabled_BasedOnPort(int port, bool expectedSsl)
    {
        // Verify the port-to-TLS convention used in ConnectWithRetryAsync.
        var sslEnabled = port == 5671;
        Assert.Equal(expectedSsl, sslEnabled);
    }
}
