using System.Collections;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Headers;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Shared.Lambda.Runtime.Filter;

public record Result<T>(bool Success, T Value);

public abstract class BaseBatchExecutionFilter<TEvent, TRecord> : IExecutionFilter {
    protected readonly IJsonSerializer JsonSerializer;
    protected readonly IMemoryStreamPool MemoryStreamPool;
    private IBatchProcessorExceptionHandler _bulkProcessorExceptionHandler;
    private readonly ILogger _logger;

    protected BaseBatchExecutionFilter(
        IJsonSerializer jsonSerializer,
        IMemoryStreamPool memoryStreamPool,
        IBatchProcessorExceptionHandler bulkProcessorExceptionHandler,
        ILogger logger) {
        JsonSerializer = jsonSerializer;
        _logger = logger;
        _bulkProcessorExceptionHandler = bulkProcessorExceptionHandler;
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
        try {
            using var inputStream = MemoryStreamPool.Get();
            using var outputStream = MemoryStreamPool.Get();
            
            var forkedChain = await WriteRequestRecord(chain, record, inputStream.Item, outputStream.Item);
            
            await forkedChain.Next();

            return forkedChain.Context.Response.Status is null or < 300;
        }
        catch (Exception exp) {
            return await _bulkProcessorExceptionHandler.HandleException(chain.Context, _logger, exp);
        }
    }

    protected abstract Task<IExecutionChain> WriteRequestRecord(IExecutionChain chain, TRecord record, Stream inputStream, Stream outputStream);

    protected abstract Task WriteResultsAsync(IExecutionContext context, IReadOnlyList<Result<TRecord>> record);

    protected abstract IEnumerable<TRecord> ReadRecords(TEvent tEvent);
}