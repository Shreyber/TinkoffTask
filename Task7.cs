namespace Task7;
interface IClient
{
    Task<IResponse> GetApplicationStatus(string id, CancellationToken cancellationToken);
}

interface IResponse
{
}

record SuccessResponse(string Id, string Status) : IResponse;
record FailureResponse() : IResponse;
record RetryResponse(TimeSpan Delay) : IResponse;

interface IHandler
{
    Task<IApplicationStatus> GetApplicationStatus(string id);
}

class Handler : IHandler
{
    private readonly IClient _service1;
    private readonly IClient _service2;
    private readonly ILogger<Handler> _logger;
    private readonly int _requestTimeout = 15;

    public Handler(
      IClient service1,
      IClient service2,
      ILogger<Handler> logger)
    {
        _service1 = service1;
        _service2 = service2;
        _loggger = logger;
    }

    private class Ref
    {
        public int value = 0;
    }

    public async Task<IApplicationStatus> GetApplicationStatus(string id)
    {
        var retriesCount = new Ref();
        var cts = new CancellationTokenSource();

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_requestTimeout), cts.Token);
        var service1Task = GetStatusFromService(_service1, id, cts.Token, retriesCount);
        var service2Task = GetStatusFromService(_service2, id, cts.Token, retriesCount);

        await Task.WhenAny(timeoutTask, service1Task, service2Task);
        var completionTime = DateTime.UtcNow;
        cts.Cancel();

        if (service1Task.IsCompletedSuccessfully)
        {
            return ReadServiceResponse(service1Task.Result, completionTime, retriesCount.value);
        }
        else if (service2Task.IsCompletedSuccessfully)
        {
            return ReadServiceResponse(service2Task.Result, completionTime, retriesCount.value);
        }
        else if (timeoutTask.IsCompleted)
        {
            _logger.LogError("Response timeout exceeded.");
            return new FailureStatus(null, retriesCount.value);
        }
        else
        {
            throw new InvalidOperationException("Unexpected task state");
        }

        async Task<IResponse> GetStatusFromService(IClient client, string id, CancellationToken cancellationToken, Ref retriesCount)
        {
            var response = await client.GetApplicationStatus(id, cancellationToken);

            if (response is RetryResponse retryResponse)
            {
                _logger.LogWarning($"Received {nameof(RetryResponse)}. Retrying after {retryResponse.Delay.TotalSeconds} seconds.");
                retriesCount.value++;
                await Task.Delay(retryResponse.Delay, cancellationToken);
                return await GetStatusFromService(client, id, cancellationToken, retriesCount);
            }

            return response;
        }

        IApplicationStatus ReadServiceResponse(IResponse response, DateTime lastRequestTime, int retriesCount)
        {
            return response switch
            {
                SuccessResponse successResponse => new SuccessStatus(successResponse.Id, successResponse.Status),
                FailureResponse => new FailureStatus(lastRequestTime, retriesCount),
                _ => throw new InvalidOperationException("Unknown response type")
            };
        }
    }
}

interface IApplicationStatus
{
}

record SuccessStatus(string ApplicationId, string Status) : IApplicationStatus;
record FailureStatus(DateTime? LastRequestTime, int RetriesCount) : IApplicationStatus;