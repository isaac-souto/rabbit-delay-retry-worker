using Polly;
using RabbitDelayRetryWorker;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using StackExchange.Redis;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton((sp) => new ConnectionFactory()
        {
            Uri = new Uri("amqp://mc:mc2@rabbitmq:5672/main"),
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            AutomaticRecoveryEnabled = true,
            DispatchConsumersAsync = true
        });

        services.AddSingleton(sp => Policy
                .Handle<BrokerUnreachableException>()
                .WaitAndRetry(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
                .Execute(() => sp.GetRequiredService<ConnectionFactory>().CreateConnection()));

        services.AddTransient((sp) => sp.GetRequiredService<IConnection>().CreateModel());

        services.AddSingleton<IConnectionMultiplexer>(provider => ConnectionMultiplexer.Connect("redis:6379", options => options.Password = "admin1234567890"));

        services.AddHostedService<Worker>();
    })
    .Build();

host.Run();
