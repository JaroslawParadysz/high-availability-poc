using Connector;
using Connector.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Connector.Tests;

public class PersistenceOptionsTests
{
    [Fact]
    public void DefaultOptions_AreUnset()
    {
        var options = new PersistenceOptions();

        Assert.Null(options.CommandTimeoutSeconds);
        Assert.Null(options.MaxRetryCount);
    }

    [Fact]
    public void Options_BindFromConfigurationSection()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["Persistence:CommandTimeoutSeconds"] = "45",
            ["Persistence:MaxRetryCount"] = "7"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.Configure<PersistenceOptions>(configuration.GetSection("Persistence"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PersistenceOptions>>().Value;

        Assert.Equal(45, options.CommandTimeoutSeconds);
        Assert.Equal(7, options.MaxRetryCount);
    }
}

public class PersistenceRegistrationTests
{
    [Fact]
    public void AddPersistence_UsesConfiguredCommandTimeout()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=test;Username=test;Password=test",
            ["Persistence:CommandTimeoutSeconds"] = "77",
            ["Persistence:MaxRetryCount"] = "4"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.AddPersistence(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();

        Assert.Equal(77, context.Database.GetCommandTimeout());
        Assert.True(context.Database.CreateExecutionStrategy().RetriesOnFailure);
    }
}
