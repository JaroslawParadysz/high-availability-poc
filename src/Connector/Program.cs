using Connector;
using Microsoft.Extensions.Options;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Configuration: Bind and validate RabbitMQ options.
        services.Configure<RabbitMqOptions>(context.Configuration.GetSection("RabbitMq"));
        services.AddOptions<RabbitMqOptions>()
            .BindConfiguration("RabbitMq")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Infrastructure: Connection provider with retry policy (Polly).
        services.AddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();

        // Business Logic: Message handler (implement with your consume → persist → publish logic).
        services.AddSingleton<IMessageHandler, DefaultMessageHandler>();

        // Setup: Queue initialization (runs once at startup before consumer starts).
        services.AddHostedService<RabbitMqQueueSetupService>();

        // Consumer: Main worker service.
        services.AddHostedService<RabbitMqConsumerWorker>();

        // Health Checks: Enable monitoring.
        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("rabbitmq");
    })
    .Build();

await host.RunAsync();
