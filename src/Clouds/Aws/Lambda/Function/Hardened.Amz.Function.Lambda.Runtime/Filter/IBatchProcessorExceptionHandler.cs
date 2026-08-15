using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Function.Lambda.Runtime.Filter;

public interface IBatchProcessorExceptionHandler {
    Task<bool> HandleException(IExecutionContext context, ILogger logger, Exception exception);
}

public class BatchProcessorExceptionHandler : IBatchProcessorExceptionHandler {
    public Task<bool> HandleException(IExecutionContext context,ILogger logger, Exception exception) {
        logger.LogError(exception, "Error while processing record");
        
        return Task.FromResult(false);
    }
}