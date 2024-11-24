using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Amz.Shared.Lambda.Runtime.Logging;

public class LambdaStructuredLogger : ILogger {
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly StructuredLogLineBuilder _structuredLogLineBuilder;

    public LambdaStructuredLogger(
        IJsonSerializer jsonSerializer,
        ILambdaContextAccessor lambdaContextAccessor,
        IStringBuilderPool stringBuilderPool,
        string categoryName) {
        _jsonSerializer = jsonSerializer;
        _lambdaContextAccessor = lambdaContextAccessor;
        _structuredLogLineBuilder = new StructuredLogLineBuilder(stringBuilderPool, categoryName);
        
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) {
        var serializedData = _structuredLogLineBuilder.Build(logLevel, eventId, state, exception, formatter);
        
        _lambdaContextAccessor.Context!.Logger.LogLine(serializedData);
    }
    
    public bool IsEnabled(LogLevel logLevel) {
        return true;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;
}
