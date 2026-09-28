using System.Text.Json;
using DependencyModules.Testing.Attributes;
using DependencyModules.xUnit.Attributes;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Aws.Lambda.Runtime.Streaming;
using Hardened.IntegrationTests.Sqs.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using LambdaHttpTestApp = Hardened.IntegrationTests.LambdaHttp.SUT.LambdaHttpTestApp;

namespace Hardened.IntegrationTests.LambdaRuntime.Tests;

/// <summary>
/// A Hardened application served over the Lambda Runtime API, which is the one path nothing else
/// covers.
///
/// <para>
/// Every other fixture reaches the handler through a façade or the invocation loop. That proves the
/// adapters, the pipeline and the routing, and skips what a deployed function actually does: poll
/// for work, read the deadline off the response headers, and post an answer or a failure back. This
/// runs <see cref="HardenedLambdaBootstrap"/> against a real socket speaking the real protocol.
/// </para>
/// </summary>
public class LambdaRuntimeLoopTests
{
    private const string OneOrder = """
        {"Records":[{
          "messageId":"m0","receiptHandle":"r0",
          "body":"{\"id\":\"a-1\",\"quantity\":4}",
          "eventSource":"aws:sqs",
          "eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders-new",
          "awsRegion":"us-east-1"
        }]}
        """;

    /// <summary>
    /// Runs the bootstrap for exactly one invocation and returns what the function posted back.
    /// </summary>
    /// <remarks>
    /// <c>AWS_LAMBDA_DOTNET_DEBUG_RUN_ONCE</c> is the runtime client's own switch for serving a
    /// single invocation and returning, which is what lets this be an ordinary awaited call rather
    /// than a background loop the test has to chase. The cancellation token is a deadline for the
    /// test itself: without one, a protocol mistake hangs the suite instead of failing it.
    /// </remarks>
    private static async Task<RuntimeApiStub.Answer> Serve(
        IServiceProvider provider,
        string payload
    )
    {
        using var runtime = new RuntimeApiStub(payload);

        PointAt(runtime);

        using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await HardenedLambdaBootstrap.Run(provider, giveUp.Token);

        return await runtime.Answered.WaitAsync(giveUp.Token);
    }

    /// <summary>
    /// Sets what the Lambda service sets for a function, with the stub as its Runtime API.
    /// </summary>
    private static void PointAt(RuntimeApiStub runtime)
    {
        Environment.SetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API", runtime.Address);
        Environment.SetEnvironmentVariable("AWS_LAMBDA_DOTNET_DEBUG_RUN_ONCE", "true");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME", "orders-function");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_MEMORY_SIZE", "512");
        Environment.SetEnvironmentVariable("AWS_LAMBDA_FUNCTION_VERSION", "$LATEST");
        Environment.SetEnvironmentVariable(
            "AWS_LAMBDA_LOG_GROUP_NAME",
            "/aws/lambda/orders-function"
        );
        Environment.SetEnvironmentVariable("AWS_LAMBDA_LOG_STREAM_NAME", "stream");
    }

    /// <summary>
    /// The whole path: poll, adapt, route, bind, handle, answer. Nothing here names an adapter, a
    /// filter or a handler - it speaks the protocol AWS speaks and checks what came back.
    /// </summary>
    [ModuleTest]
    public async Task AnInvocationOverTheRuntimeApiReachesTheHandler(
        IServiceProvider provider,
        [Mock] IOrderStore store
    )
    {
        var answer = await Serve(provider, OneOrder);

        Assert.False(answer.Failed);

        store.Received().Place(Arg.Is<Order>(order => order.Id == "a-1" && order.Quantity == 4));
    }

    /// <summary>The batch report is what goes back on the wire, not just what the adapter can produce.</summary>
    [ModuleTest]
    public async Task TheBatchReportIsWhatThePostContains(
        IServiceProvider provider,
        [Mock] IOrderStore store
    )
    {
        var answer = await Serve(provider, OneOrder);

        using var report = JsonDocument.Parse(answer.Body);

        Assert.Empty(report.RootElement.GetProperty("batchItemFailures").EnumerateArray());
    }

    /// <summary>
    /// The failure policy arriving where it matters. An event adapter rethrows, and a rethrow has
    /// to reach AWS as a posted invocation error - that is what returns the message to the queue.
    /// Answering with a 200 and a body describing the failure would tell SQS to delete it.
    /// </summary>
    [ModuleTest]
    public async Task AFailedHandlerPostsAnInvocationError(
        IServiceProvider provider,
        [Mock] IOrderStore store
    )
    {
        store
            .When(one => one.Place(Arg.Any<Order>()))
            .Do(_ => throw new InvalidOperationException("handler refused the order"));

        var answer = await Serve(provider, OneOrder);

        Assert.True(answer.Failed);
        Assert.Contains("handler refused the order", answer.Body);
    }

    /// <summary>A startup service that records that it ran, and nothing else.</summary>
    private sealed class Probe : IStartupService
    {
        public bool Ran;

        public Task<bool> Startup(IServiceProvider rootProvider)
        {
            Ran = true;

            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// The startup services run, over a provider the function built itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its own provider rather than the injected one, and that is the whole point. Every test host
    /// in this repository calls <c>ApplicationLogic.Start</c> before it hands a provider out, so a
    /// test taking one asserts against a container something else already started - which is why
    /// nothing here noticed that the bootstrap never started one. This builds the container the way
    /// <c>Program.cs</c> does and leaves the bootstrap as the only thing that could have run them.
    /// </para>
    /// <para>
    /// The startup set is the middleware chain: authentication installs its middleware there,
    /// authorization installs its filter provider, CORS installs its configuration. A function that
    /// skips it serves every request anonymous and every route open, with nothing logged.
    /// </para>
    /// <para>
    /// In this class rather than a file of its own, because the runtime client reads its address
    /// from the environment and xUnit runs two classes as two collections in parallel - which is two
    /// tests writing one process-wide variable.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheBootstrapRunsTheStartupServicesOnTheProviderItIsGiven()
    {
        var probe = new Probe();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));
        services.AddSingleton(Substitute.For<IOrderStore>());
        services.AddSingleton<IStartupService>(probe);

        new SqsTestApp().PopulateServiceCollection(services);

        Assert.False(probe.Ran);

        await Serve(services.BuildServiceProvider(), OneOrder);

        Assert.True(probe.Ran);
    }

    /// <summary>
    /// One HTTP function in mixed mode, as the Runtime API receives it from AWS's own runtime client:
    /// an event stream posted as a streamed response, and an order posted as one payload.
    /// </summary>
    /// <remarks>
    /// The invocation handler's tests stop at <c>IResponseStreamFactory</c>. This is what the runtime
    /// client does on either side of it, which is what a front door taking both kinds of answer has
    /// to read.
    /// </remarks>
    [Fact]
    public async Task InMixedModeAnEventStreamIsPostedStreamedAndAnOrderAsOnePayload()
    {
        var provider = HttpFunction(LambdaResponseMode.Mixed);

        var events = await Serve(provider, HttpEvent("/orders/live"));
        var order = await Serve(provider, HttpEvent("/orders/o-1"));

        Assert.True(events.Streamed);

        var (prelude, body) = Split(events.Body);

        Assert.Equal(200, prelude.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Contains("text/event-stream", prelude.RootElement.GetProperty("headers").ToString());
        Assert.Equal("data: {\"id\":\"live-1\",\"quantity\":1}\n\n", body);

        Assert.False(order.Streamed);

        using var envelope = JsonDocument.Parse(order.Body);

        Assert.Equal(200, envelope.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Contains("\"o-1\"", envelope.RootElement.GetProperty("body").GetString());
    }

    /// <summary>
    /// The control: in stream mode the same order is posted as a streamed response.
    /// </summary>
    [Fact]
    public async Task InStreamModeAnOrderIsPostedStreamed()
    {
        var order = await Serve(HttpFunction(LambdaResponseMode.Stream), HttpEvent("/orders/o-1"));

        Assert.True(order.Streamed);
        Assert.Contains("\"o-1\"", Split(order.Body).Body);
    }

    /// <summary>
    /// The HTTP fixture built the way its <c>Program.cs</c> builds it, in the given mode.
    /// </summary>
    private static IServiceProvider HttpFunction(LambdaResponseMode mode)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));

        new LambdaHttpTestApp().PopulateServiceCollection(services);

        services.ConfigureLambdaResponseMode(configuration => configuration.Mode = mode);

        return services.BuildServiceProvider();
    }

    /// <summary>A GET as a function URL delivers it, in payload format 2.0.</summary>
    private static string HttpEvent(string path) =>
        $$"""
            {"version":"2.0","rawPath":"{{path}}","rawQueryString":"","headers":{},
             "requestContext":{"domainName":"function.test",
              "http":{"method":"GET","path":"{{path}}","protocol":"HTTP/1.1","sourceIp":"203.0.113.7"} },
             "isBase64Encoded":false}
            """;

    /// <summary>
    /// A streamed HTTP response's prelude and body, which the eight null bytes separate.
    /// </summary>
    private static (JsonDocument Prelude, string Body) Split(string streamed)
    {
        var separator = streamed.IndexOf("\0\0\0\0\0\0\0\0", StringComparison.Ordinal);

        Assert.True(separator > 0, "the streamed response has no prelude");

        return (JsonDocument.Parse(streamed[..separator]), streamed[(separator + 8)..]);
    }

    /// <summary>A startup service that logs one entry, with one value in its message.</summary>
    private sealed class Warmer(ILogger<Warmer> logger) : IStartupService
    {
        public Task<bool> Startup(IServiceProvider rootProvider)
        {
            logger.LogInformation("Warmed {Count} caches", 3);

            return Task.FromResult(true);
        }
    }

    /// <summary>A startup service that fails.</summary>
    private sealed class Refuser : IStartupService
    {
        public Task<bool> Startup(IServiceProvider rootProvider) =>
            throw new InvalidOperationException("the cache would not warm");
    }

    /// <summary>
    /// The function the way the templates build it: a container of its own, logging through the
    /// Lambda logger.
    /// </summary>
    private static IServiceProvider Function(Action<IServiceCollection> add)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
            builder.AddLambdaLogger(new LambdaLoggerOptions { IncludeException = true })
        );
        services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));
        services.AddSingleton(Substitute.For<IOrderStore>());

        add(services);

        new SqsTestApp().PopulateServiceCollection(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Serves one invocation with the function's log format set to <paramref name="logFormat"/>,
    /// and returns the lines the runtime wrote.
    /// </summary>
    /// <remarks>
    /// <c>AWS_LAMBDA_DOTNET_DISABLE_CONSOLE_CAPTURE</c> is the runtime client's own switch for
    /// leaving <c>Console.Out</c> alone. Its writer then writes to the <c>Console.Out</c> it found
    /// when the bootstrap was built, which is the writer set here.
    /// </remarks>
    private static async Task<string[]> ServeAndRead(IServiceProvider provider, string? logFormat)
    {
        var console = Console.Out;
        using var log = new StringWriter();

        Environment.SetEnvironmentVariable("AWS_LAMBDA_LOG_FORMAT", logFormat);
        Environment.SetEnvironmentVariable("AWS_LAMBDA_DOTNET_DISABLE_CONSOLE_CAPTURE", "true");
        Console.SetOut(log);

        try
        {
            await Serve(provider, OneOrder);
        }
        finally
        {
            Console.SetOut(console);
            Environment.SetEnvironmentVariable("AWS_LAMBDA_LOG_FORMAT", null);
            Environment.SetEnvironmentVariable("AWS_LAMBDA_DOTNET_DISABLE_CONSOLE_CAPTURE", null);
        }

        return log.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// A startup service's entry is written by the runtime, in the function's log format.
    /// </summary>
    /// <remarks>
    /// Building the bootstrap is what installs the runtime's writer. A startup service that runs
    /// before then logs through Amazon.Lambda.Core's fallback, which writes a text line with
    /// <c>{Count}</c> unfilled even on a JSON function, and this test then finds no record.
    /// </remarks>
    [Fact]
    public async Task AStartupServiceLogsInTheFunctionsLogFormat()
    {
        var lines = await ServeAndRead(
            Function(services => services.AddSingleton<IStartupService, Warmer>()),
            "JSON"
        );

        var entry = lines
            .Where(line => line.StartsWith('{'))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line))
            .Single(entry => entry.TryGetProperty("Count", out _));

        Assert.Equal("Information", entry.GetProperty("level").GetString());
        Assert.Equal(3, entry.GetProperty("Count").GetInt32());
        Assert.Contains("Warmed 3 caches", entry.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AFunctionThatLogsTextIsWarnedAtStartup()
    {
        var lines = await ServeAndRead(Function(_ => { }), logFormat: null);

        Assert.Contains(
            lines,
            line =>
                line.Contains(
                    "This function logs in Lambda's text format, because AWS_LAMBDA_LOG_FORMAT is not set."
                )
        );
    }

    [Fact]
    public async Task AFunctionThatLogsJsonIsNotWarned()
    {
        var lines = await ServeAndRead(Function(_ => { }), "JSON");

        Assert.DoesNotContain(lines, line => line.Contains("logs in Lambda's text format"));
    }

    /// <summary>
    /// A startup service that throws is reported to Lambda as an init error, not only thrown in this
    /// process.
    /// </summary>
    [Fact]
    public async Task AStartupServiceThatThrowsIsReportedAsAnInitError()
    {
        var provider = Function(services => services.AddSingleton<IStartupService, Refuser>());

        using var runtime = new RuntimeApiStub(OneOrder);

        PointAt(runtime);

        using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HardenedLambdaBootstrap.Run(provider, giveUp.Token)
        );

        // The runtime client posts the error before it rethrows, so it has arrived by now.
        Assert.True(runtime.InitFailed.IsCompleted, "Nothing was posted to /runtime/init/error.");
        Assert.Contains(
            "the cache would not warm",
            await runtime.InitFailed.WaitAsync(giveUp.Token)
        );
    }
}
