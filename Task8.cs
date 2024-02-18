namespace Task8;
interface IHandler
{
    TimeSpan Timeout { get; }

    Task PerformOperation(CancellationToken cancellationToken);
}

class Handler : IHandler
{
    private readonly IConsumer _consumer;
    private readonly IPublisher _publisher;
    private readonly ILogger<Handler> _logger;

    public TimeSpan Timeout { get; }

    public Handler(
      TimeSpan timeout,
      IConsumer consumer,
      IPublisher publisher,
      Ilogger<Handler> logger)
    {
        Timeout = timeout;

        _consumer = consumer;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task PerformOperation(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var data = await _consumer.ReadData();

                if (data != null)
                {
                    await ProcessData(data, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during PerformOperation.");
        }

        async Task ProcessData(Event eventData, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();

            foreach (var recipient in eventData.Recipients)
            {
                tasks.Add(PublishData(recipient, eventData.Payload));
            }

            await Task.WhenAll(tasks);
        }

        async Task PublishData(Address recipient, Payload payload)
        {
            var result = await _publisher.SendData(recipient, payload);
            if (result == SendResult.Rejected)
            {
                _logger.LogWarning($"Rejected sending data to {recipient}. Retrying after {Timeout.TotalSeconds} seconds.");
                await Task.Delay(Timeout, cancellationToken);
                await PublishData(recipient, payload);
            }
            else
            {
                _logger.LogInformation($"Data successfully sent to {recipient}.");
            }
        }
    }
}

record Payload(string Origin, byte[] Data);
record Address(string DataCenter, string NodeId);
record Event(IReadOnlyCollection<Address> Recipients, Payload Payload);

enum SendResult
{
    Accepted,
    Rejected
}

interface IConsumer
{
    Task<Event> ReadData();
}

interface IPublisher
{
    Task<SendResult> SendData(Address address, Payload payload);
}