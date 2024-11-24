using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Amz.Shared.Lambda.Runtime.Logging;

public class LambdaLoggerProvider : ILoggerProvider {
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly IJsonSerializer _serializer;
    private readonly IStringBuilderPool _stringBuilderPool;

    private readonly ConcurrentDictionary<string, LambdaStructuredLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    public LambdaLoggerProvider(ILambdaContextAccessor lambdaContextAccessor, IJsonSerializer serializer, IStringBuilderPool stringBuilderPool) {
        _lambdaContextAccessor = lambdaContextAccessor;
        _serializer = serializer;
        _stringBuilderPool = stringBuilderPool;
    }

    public void Dispose() { }

    public ILogger CreateLogger(string categoryName) {
        return _loggers.GetOrAdd(categoryName,
            categoryString => new LambdaStructuredLogger(
                _serializer, 
                _lambdaContextAccessor,
                _stringBuilderPool, 
                categoryString));
    }
}