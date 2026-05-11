using Connector;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.Configure<RabbitMqOptions>(context.Configuration.GetSection("RabbitMq"));
        services.AddHostedService<RabbitMqConsumerWorker>();
    })
    .Build();

await host.RunAsync();
