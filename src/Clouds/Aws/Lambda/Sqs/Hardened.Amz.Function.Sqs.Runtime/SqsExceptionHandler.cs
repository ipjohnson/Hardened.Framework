using Amazon.Lambda.SQSEvents;
using Hardened.Requests.Abstract.Execution;
using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Function.Sqs.Runtime;

public interface ISqsExceptionHandler {
    ValueTask<bool> HandleException(IExecutionChain chain, SQSEvent.SQSMessage message, Exception exception);
}

[SingletonService]
public class SqsExceptionHandler : ISqsExceptionHandler {
    private readonly ValueTask<bool> _defaultResult = new (false);
    private readonly ILogger<SqsExceptionHandler> _logger;

    public SqsExceptionHandler(ILogger<SqsExceptionHandler> logger) {
        _logger = logger;
    }

    public ValueTask<bool> HandleException(
        IExecutionChain chain, 
        SQSEvent.SQSMessage message, 
        Exception exception) {
        
        _logger.LogError(exception, "SQS exception thrown {Message}", exception.Message);
        
        return _defaultResult;
    }
}