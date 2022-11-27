using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Hardened.Shared.Testing.Logging;

public class XunitLoggerProvider : ILoggerProvider
{
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly ConcurrentDictionary<string, XUnitLogger> _loggers = new();

    public XunitLoggerProvider(IJsonSerializer jsonSerializer, ITestOutputHelper testOutputHelper)
    {
        _jsonSerializer = jsonSerializer;
        _testOutputHelper = testOutputHelper;
    }

    public void Dispose()
    {
        
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(
            categoryName, 
            s => new XUnitLogger(_jsonSerializer, s, _testOutputHelper));
    }
}