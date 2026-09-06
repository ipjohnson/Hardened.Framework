using Amazon.Lambda.Core;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Amz.Shared.Lambda.Runtime.Logging;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using System.Text.Json;
using Xunit;

namespace Hardened.Amz.Shared.Lambda.Runtime.Tests.Logging;

public class LambdaStructuredLoggerTests
{
    private readonly List<string> _logLines = new();

    private LambdaStructuredLogger CreateLogger()
    {
        var jsonSerializer = Substitute.For<IJsonSerializer>();
        var lambdaContext = Substitute.For<ILambdaContext>();
        var lambdaLogger = Substitute.For<ILambdaLogger>();
        lambdaLogger.When(x => x.LogLine(Arg.Any<string>()))
            .Do(ci => _logLines.Add(ci.Arg<string>()));
        lambdaContext.Logger.Returns(lambdaLogger);

        var accessor = Substitute.For<ILambdaContextAccessor>();
        accessor.Context.Returns(lambdaContext);

        var stringBuilderPool = new StringBuilderPool();
        return new LambdaStructuredLogger(jsonSerializer, accessor, stringBuilderPool, "TestLogger");
    }

    [Fact]
    public void BeginScope_Direct_ScopePropertiesAppearInLog()
    {
        var logger = CreateLogger();

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["userId"] = "user-123",
            ["sessionId"] = "sess-456",
            ["requestId"] = "req-789"
        }))
        {
            logger.Log(LogLevel.Information, new EventId(0),
                new List<KeyValuePair<string, object?>> { new("{OriginalFormat}", "test") },
                null, (s, e) => "inside scope");
        }

        Assert.Single(_logLines);
        var doc = JsonDocument.Parse(_logLines[0]);
        var root = doc.RootElement;

        Assert.Equal("user-123", root.GetProperty("userId").GetString());
        Assert.Equal("sess-456", root.GetProperty("sessionId").GetString());
        Assert.Equal("req-789", root.GetProperty("requestId").GetString());
        Assert.Equal("inside scope", root.GetProperty("message").GetString());
    }

    [Fact]
    public void BeginScope_ViaILogger_ScopePropertiesAppearInLog()
    {
        var logger = CreateLogger();
        ILogger ilogger = logger; // call through the interface

        using (ilogger.BeginScope(new Dictionary<string, object?>
        {
            ["userId"] = "user-123",
        }))
        {
            ilogger.LogInformation("inside scope via ILogger");
        }

        Assert.Single(_logLines);
        var doc = JsonDocument.Parse(_logLines[0]);
        var root = doc.RootElement;

        Assert.Equal("user-123", root.GetProperty("userId").GetString());
    }

    [Fact]
    public void BeginScope_ViaMSLoggingFactory_ScopePropertiesAppearInLog()
    {
        // This tests the full MS Logging pipeline
        var jsonSerializer = Substitute.For<IJsonSerializer>();
        var lambdaContext = Substitute.For<ILambdaContext>();
        var lambdaLogger = Substitute.For<ILambdaLogger>();
        lambdaLogger.When(x => x.LogLine(Arg.Any<string>()))
            .Do(ci => _logLines.Add(ci.Arg<string>()));
        lambdaContext.Logger.Returns(lambdaLogger);

        var accessor = Substitute.For<ILambdaContextAccessor>();
        accessor.Context.Returns(lambdaContext);

        var stringBuilderPool = new StringBuilderPool();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILoggerProvider>(new LambdaLoggerProvider(accessor, jsonSerializer, stringBuilderPool));

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<LambdaStructuredLoggerTests>>();

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["userId"] = "user-123",
            ["sessionId"] = "sess-456",
        }))
        {
            logger.LogInformation("inside scope via factory");
        }

        Assert.Single(_logLines);
        var doc = JsonDocument.Parse(_logLines[0]);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("userId", out var userIdProp),
            $"Expected 'userId' in log output. Full log: {_logLines[0]}");
        Assert.Equal("user-123", userIdProp.GetString());
    }
}
