using Hardened.Amz.Function.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Headers;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Function.Lambda.Runtime.Filter;

public record Result<T>(bool Success, T Value);

public abstract class BaseBatchExecutionFilter<TEvent, TRecord> : IExecutionFilter {
    /// <summary>
    /// Matches the namespace the invoke engines and the API Gateway processor create their
    /// loggers under, so a record's timings land beside every other request's.
    /// </summary>
    private const string MetricNamespace = "HardenedRequests";

    protected readonly IJsonSerializer JsonSerializer;
    protected readonly IMemoryStreamPool MemoryStreamPool;
    private readonly IBatchProcessorExceptionHandler _batchProcessorExceptionHandler;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly ILogger _logger;

    protected BaseBatchExecutionFilter(
        IJsonSerializer jsonSerializer,
        IMemoryStreamPool memoryStreamPool,
        IBatchProcessorExceptionHandler batchProcessorExceptionHandler,
        IMetricLoggerProvider metricLoggerProvider,
        ILogger logger) {
        JsonSerializer = jsonSerializer;
        _logger = logger;
        _batchProcessorExceptionHandler = batchProcessorExceptionHandler;
        _metricLoggerProvider = metricLoggerProvider;
        MemoryStreamPool = memoryStreamPool;
    }

    public async Task Execute(IExecutionChain chain) {
        var incomingEvent = await JsonSerializer.DeserializeAsync<TEvent>(chain.Context.Request.Body);
        var results = new List<Result<TRecord>>();

        foreach (var record in ReadRecords(incomingEvent)) {
            var success = await ProcessRecord(chain, record);

            results.Add(new(success, record));
        }

        await WriteResultsAsync(chain.Context, results);
    }

    private async Task<bool> ProcessRecord(IExecutionChain chain, TRecord record) {
        // A sink per record, not per batch. Clone falls back to the batch's logger when it is not
        // given one, and the filters inside the chain - IOFilter, the invoke filters - record
        // through whatever the context carries. EmbeddedMetricLogger writes its values into a
        // dictionary keyed by metric name, so a shared sink does not accumulate ten data points:
        // the tenth record's HandlerInvokeDuration overwrites the ninth, and the EMF header ends up
        // declaring the same metric once per record. One logger each means one EMF line each, which
        // is what CloudWatch needs to compute a percentile across a batch.
        //
        // Disposed either way - Dispose is what writes the line, so a record that throws would
        // otherwise report nothing at all.
        using var metricLogger = _metricLoggerProvider.CreateLogger(MetricNamespace);

        try {
            using var inputStream = MemoryStreamPool.Get();
            using var outputStream = MemoryStreamPool.Get();

            await WriteRequestRecord(chain, record, inputStream.Item);

            var request =
                new LambdaExecutionRequest(
                    chain.Context.Request.Method, chain.Context.Request.Path, inputStream.Item, chain.Context.Request.Headers);
            var response =
                new LambdaExecutionResponse(outputStream.Item, new HeaderCollectionStringValues());

            var context = chain.Context.Clone(request, response, metricLogger: metricLogger);

            var forkedChain = chain.Fork(context);

            await forkedChain.Next();

            return forkedChain.Context.Response.Status is null or < 300;
        }
        catch (Exception exp) {
            return await _batchProcessorExceptionHandler.HandleException(chain.Context, _logger, exp);
        }
    }

    protected abstract Task WriteRequestRecord(IExecutionChain chain, TRecord record, Stream inputStream);

    protected abstract Task WriteResultsAsync(IExecutionContext context, IReadOnlyList<Result<TRecord>> record);

    protected abstract IEnumerable<TRecord> ReadRecords(TEvent tEvent);
}