using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Hardened.Amz.Shared.Lambda.Runtime.Logging;

public class LambdaLoggerProvider : ILoggerProvider
{
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly IJsonSerializer _serializer;
    private readonly ConcurrentDictionary<string, LambdaStructuredLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);
    
    public LambdaLoggerProvider(ILambdaContextAccessor lambdaContextAccessor, IJsonSerializer serializer)
    {
        _lambdaContextAccessor = lambdaContextAccessor;
        _serializer = serializer;
    }

    public void Dispose()
    {
        
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, 
            categoryString => new LambdaStructuredLogger(_serializer, _lambdaContextAccessor, categoryString));
    }
}