using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;

namespace RabbitDelayRetryWorker
{
    public class Worker : BackgroundService
    {
        readonly IModel _model;

        readonly IServiceScopeFactory _factory;

        public Worker(IModel model, IServiceScopeFactory factory)
        {
            _model = model;
            _factory = factory;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            //Unrouted
            _model.ExchangeDeclare("order.unrouted", "fanout", true, false);
            _model.QueueDeclare("order.unrouted", true, false, false, new Dictionary<string, object> { { "x-queue-type", "quorum" } });
            _model.QueueBind("order.unrouted", "order.unrouted", string.Empty);

            //Deadletter
            _model.ExchangeDeclare("order.deadletter", "fanout", true, false);
            _model.QueueDeclare("order.deadletter", true, false, false, new Dictionary<string, object> { { "x-queue-type", "quorum" } });
            _model.QueueBind("order.deadletter", "order.deadletter", string.Empty);

            //Exchange            
            _model.ConfirmSelect();
            _model.BasicQos(0, 10, false);
            _model.ExchangeDeclare("order", "topic", true, false, new Dictionary<string, object> { { "alternate-exchange", "order.unrouted" } });
            _model.QueueDeclare("order", true, false, false, new Dictionary<string, object>
            {
                { "x-queue-type", "quorum" },
                { "x-dead-letter-exchange", "order.deadletter" },
                { "x-delivery-limit", 3 }
            });
            _model.QueueBind("order", "order", "create");

            AsyncEventingBasicConsumer consumer = new(_model);

            consumer.Received += async (object _, BasicDeliverEventArgs eventArgs) =>
            {
                await using AsyncServiceScope AsyncScope = _factory.CreateAsyncScope();

                var Connection = AsyncScope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();

                var Cache = Connection.GetDatabase();

                try
                {
                    bool Lock = false;
                    using PeriodicTimer Delay = new(TimeSpan.FromSeconds(1));

                    do
                    {
                        Lock = await Cache.KeyExistsAsync(eventArgs.BasicProperties.MessageId);
                        await Delay.WaitForNextTickAsync(cancellationToken);
                    } while (Lock);

                    throw new Exception("Um erro ocorreu");
                }
                catch
                {
                    await Cache.StringSetAsync(eventArgs.BasicProperties.MessageId, string.Empty, TimeSpan.FromSeconds(20));

                    _model.BasicReject(eventArgs.DeliveryTag, true);
                }
            };

            _model.BasicConsume("order", false, consumer);

            //Publish Message
            IBasicProperties Props = _model.CreateBasicProperties();
            Props.DeliveryMode = 2;
            Props.ContentType = "application/json";
            Props.MessageId = Guid.NewGuid().ToString();

            _model.BasicPublish("order", "create", Props, null);

            using PeriodicTimer Timer = new(TimeSpan.FromSeconds(1));

            while (!cancellationToken.IsCancellationRequested) await Timer.WaitForNextTickAsync(cancellationToken);
        }
    }
}