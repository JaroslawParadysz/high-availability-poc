using System.Data.Common;
using System.Threading;
using Connector.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace Connector.Tests;

public sealed class CommunicationLogHandlerIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("connector_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task HandleAsync_EndToEnd_PersistsProcessedRow()
    {
        await ResetDatabaseAsync();

        using var provider = BuildProvider();
        var handler = CreateHandler(provider);

        var correlationId = Guid.NewGuid().ToString();
        await handler.HandleAsync("payload", correlationId, CancellationToken.None);

        await using var assertDb = CreateDbContext();
        var row = await assertDb.CommunicationLogs.SingleAsync();

        Assert.Equal(Guid.Parse(correlationId), row.CorrelationId);
        Assert.Equal("processed", row.Status);
    }

    [Fact]
    public async Task HandleAsync_DuplicateDelivery_PersistsDuplicateEvent()
    {
        await ResetDatabaseAsync();

        using var provider = BuildProvider();
        var handler = CreateHandler(provider);

        var correlationId = Guid.NewGuid().ToString();
        await handler.HandleAsync("payload", correlationId, CancellationToken.None);
        await handler.HandleAsync("payload", correlationId, CancellationToken.None);

        await using var assertDb = CreateDbContext();
        Assert.Equal(1, await assertDb.CommunicationLogs.CountAsync());
        Assert.Equal(1, await assertDb.DuplicateEvents.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_ProcessedInsertFailure_PersistsFailedStatusRow()
    {
        await ResetDatabaseAsync();

        var interceptor = new FailProcessedInsertInterceptor();
        using var provider = BuildProvider(interceptor);
        var handler = CreateHandler(provider);

        var correlationId = Guid.NewGuid().ToString();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync("payload", correlationId, CancellationToken.None));

        await using var assertDb = CreateDbContext();
        var row = await assertDb.CommunicationLogs.SingleAsync();

        Assert.Equal("failed", row.Status);
        Assert.Contains("Simulated processed insert failure", exception.Message);
        Assert.Contains("Simulated processed insert failure", row.ErrorMessage);
    }

    private ServiceProvider BuildProvider(params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();

        services.AddDbContext<ConnectorDbContext>(options =>
        {
            options
                .UseNpgsql(_postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention();

            if (interceptors.Length > 0)
            {
                options.AddInterceptors(interceptors);
            }
        });

        return services.BuildServiceProvider();
    }

    private static CommunicationLogHandler CreateHandler(ServiceProvider provider)
    {
        return new CommunicationLogHandler(
            NullLogger<CommunicationLogHandler>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RabbitMqOptions { QueueName = "test.queue" }));
    }

    private ConnectorDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention();

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new ConnectorDbContext(builder.Options);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE duplicate_events, communication_log RESTART IDENTITY;");
    }

    private sealed class FailProcessedInsertInterceptor : DbCommandInterceptor
    {
        private int _hasFailed;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _hasFailed, 1) == 0
                && command.CommandText.Contains("INSERT INTO communication_log", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Simulated processed insert failure");
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
