using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hardened.Amz.Shared.Lambda.Runtime.Logging;

public class LambdaStructuredLogger : ILogger
{
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly string _categoryName;

    public LambdaStructuredLogger(IJsonSerializer jsonSerializer, ILambdaContextAccessor lambdaContextAccessor, string categoryName)
    {
        _jsonSerializer = jsonSerializer;
        _lambdaContextAccessor = lambdaContextAccessor;
        _categoryName = categoryName;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        string serializedData = "null";
        switch (state)
        {
            case IEnumerable<Tuple<string,object>> data:
                var dataTuple = GetRecordDataTuple(logLevel, eventId, state, data, exception, formatter);

                var bytes = JsonSerializer.SerializeToUtf8Bytes(dataTuple,
                    RecordSerializer.Default.StructuredTupleData);

                serializedData = Encoding.UTF8.GetString(bytes);
                break;
            default:
                
                var record = GetRecord(logLevel, eventId, state, exception, formatter);
                
                serializedData = _jsonSerializer.Serialize(record);
                break;
        }
        
        _lambdaContextAccessor.Context!.Logger.LogLine(serializedData);
    }

    private StructuredTupleData GetRecordDataTuple<TState>(LogLevel logLevel, EventId eventId, TState state, IEnumerable<Tuple<string,object>> data, Exception? exception, Func<TState,Exception,string> formatter)
    {        ExceptionRecord? exceptionRecord = null;
        
        if (exception != null)
        {
            exceptionRecord = new ExceptionRecord(
                exception.GetType().Name,
                exception.Message,
                exception.StackTrace ?? "empty"
            );
        }

        return new StructuredTupleData(
            DateTime.UtcNow,
            _lambdaContextAccessor.Context!.AwsRequestId,
            _categoryName,
            logLevel.ToString(),
            eventId,
            formatter(state, exception!),
            data,
            exceptionRecord
        );

    }

    private StructuredLogEntry<TState> GetRecord<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception, string> formatter)
    {
        ExceptionRecord? exceptionRecord = null;
        
        if (exception != null)
        {
            exceptionRecord = new ExceptionRecord(
                exception.GetType().Name,
                exception.Message,
                exception.StackTrace ?? "empty"
            );
        }

        return new StructuredLogEntry<TState>(
            DateTime.UtcNow,
            _lambdaContextAccessor.Context!.AwsRequestId,
            _categoryName,
            logLevel.ToString(),
            eventId,
            formatter(state, exception!),
            state,
            exceptionRecord
        );
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

}

public partial class StructuredTupleData
{
    public StructuredTupleData(DateTime timestamp, string requestId, string logger, string logLevel, EventId eventId, string message, IEnumerable<Tuple<string, object>> data, ExceptionRecord? exception)
    {
        Timestamp = timestamp;
        RequestId = requestId;
        Logger = logger;
        LogLevel = logLevel;
        EventId = eventId;
        Message = message;
        Data = data;
        Exception = exception;
    }

    public DateTime Timestamp { get;  }
    public string RequestId { get; }
    public string Logger { get; }

    public string LogLevel { get;  }
    public EventId EventId { get; }

    public string Message { get;  }
    public IEnumerable<Tuple<string,object>> Data { get;  }
    public ExceptionRecord? Exception { get; }
}

public class StructuredLogEntry<TState>
{
    public StructuredLogEntry(
        DateTime timestamp, 
        string requestId, string logger, string logLevel, EventId eventId, string message, TState data, ExceptionRecord? exception)
    {
        Timestamp = timestamp;
        RequestId = requestId;
        Logger = logger;
        LogLevel = logLevel;
        EventId = eventId;
        Message = message;
        Data = data;
        Exception = exception;
    }

    public DateTime Timestamp { get; }
    public string RequestId { get; }
    public string Logger { get; }

    public string LogLevel { get; }
    public EventId EventId { get; }

    public string Message { get; }
    public TState Data { get; }
    public ExceptionRecord? Exception { get; }
}

public record ExceptionRecord(
    string Type,
    string Message,
    string StackTrace);

public class Testing
{
    
}
