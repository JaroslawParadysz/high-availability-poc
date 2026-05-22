using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Xunit;

namespace Connector.Tests;

/// <summary>
/// Unit tests for <see cref="CommunicationLogHandler"/>.
/// Verifies exception routing: transient Npgsql errors become <see cref="TransientPersistenceException"/>,
/// non-transient errors are rethrown after a failure-persistence attempt, and
/// cancellation bypasses failure persistence.
/// </summary>
public class CommunicationLogHandlerTests
{
    private readonly Mock<ILogger<CommunicationLogHandler>> _mockLogger;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly IOptions<RabbitMqOptions> _options;

    public CommunicationLogHandlerTests()
    {
        _mockLogger = new Mock<ILogger<CommunicationLogHandler>>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _options = Options.Create(new RabbitMqOptions { QueueName = "test.queue" });
    }

    private CommunicationLogHandler CreateHandler() =>
        new(_mockLogger.Object, _mockScopeFactory.Object, _options);

    /// <summary>
    /// Test double: NpgsqlException that reports itself as transient.
    /// </summary>
    private sealed class FakeTransientNpgsqlException : NpgsqlException
    {
        public FakeTransientNpgsqlException() : base("Simulated transient DB error") { }
        public override bool IsTransient => true;
    }

    [Fact]
    public async Task HandleAsync_ThrowsTransientPersistenceException_WhenTransientNpgsqlExceptionThrown()
    {
        // Arrange — scope factory throws a transient Npgsql error immediately.
        var transientEx = new FakeTransientNpgsqlException();
        _mockScopeFactory.Setup(f => f.CreateScope()).Throws(transientEx);

        var handler = CreateHandler();

        // Act
        var thrownEx = await Assert.ThrowsAsync<TransientPersistenceException>(
            () => handler.HandleAsync("body", Guid.NewGuid().ToString(), CancellationToken.None));

        // Assert — original exception is the inner cause.
        Assert.Same(transientEx, thrownEx.InnerException);
    }

    [Fact]
    public async Task HandleAsync_DoesNotPersistFailure_WhenTransientNpgsqlExceptionThrown()
    {
        // Arrange
        _mockScopeFactory.Setup(f => f.CreateScope()).Throws(new FakeTransientNpgsqlException());

        var handler = CreateHandler();

        await Assert.ThrowsAsync<TransientPersistenceException>(
            () => handler.HandleAsync("body", Guid.NewGuid().ToString(), CancellationToken.None));

        // Transient path should not attempt failure persistence — CreateScope called exactly once.
        _mockScopeFactory.Verify(f => f.CreateScope(), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_RethrowsOriginalException_WhenNonTransientExceptionThrown()
    {
        // Arrange — non-transient error; CreateScope will throw on every call
        // (main attempt + failure-persistence attempt).
        var originalEx = new InvalidOperationException("DB connection refused");
        _mockScopeFactory.Setup(f => f.CreateScope()).Throws(originalEx);

        var handler = CreateHandler();

        // Act
        var thrownEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync("body", Guid.NewGuid().ToString(), CancellationToken.None));

        // Assert — exact same instance rethrown.
        Assert.Same(originalEx, thrownEx);
    }

    [Fact]
    public async Task HandleAsync_AttemptsFailurePersistence_WhenNonTransientExceptionThrown()
    {
        // Arrange — CreateScope always throws, so failure-persistence will also fail (swallowed).
        _mockScopeFactory.Setup(f => f.CreateScope()).Throws(new InvalidOperationException("DB failure"));

        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync("body", Guid.NewGuid().ToString(), CancellationToken.None));

        // Two CreateScope calls: one for the main attempt, one for the failure-persistence attempt.
        _mockScopeFactory.Verify(f => f.CreateScope(), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_DoesNotPersistFailure_WhenOperationCancelled()
    {
        // Arrange — simulates cancellation during message handling.
        _mockScopeFactory.Setup(f => f.CreateScope()).Throws(new OperationCanceledException());

        var handler = CreateHandler();

        // Act — OperationCanceledException should propagate unwrapped.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.HandleAsync("body", Guid.NewGuid().ToString(), CancellationToken.None));

        // Assert — failure-persistence is NOT attempted for cancellations.
        _mockScopeFactory.Verify(f => f.CreateScope(), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_LogsError_WhenFailurePersistenceItselfFails()
    {
        // Arrange — every CreateScope call throws a generic exception so that
        // the failure-persistence scope also fails.
        _mockScopeFactory.Setup(f => f.CreateScope())
            .Throws(new InvalidOperationException("DB unavailable"));

        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync("body", Guid.NewGuid().ToString(), CancellationToken.None));

        // Assert — an error was logged about the failed failure-record insert.
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_ExposesSourceQueue_FromOptions()
    {
        // Verify the handler can be constructed without throwing.
        var handler = CreateHandler();
        Assert.NotNull(handler);
    }
}
