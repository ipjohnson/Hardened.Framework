using Amazon.Lambda.SQSEvents;
using Hardened.Amz.Function.Lambda.Runtime.Filter;
using Hardened.Amz.Function.Sqs.Runtime.Impl;
using Hardened.Amz.Function.Sqs.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Amz.Function.Sqs.Runtime.Tests;

/// <summary>
/// The batch filter has to be registered globally and ahead of everything else. It deserialises the
/// SQS envelope and forks a chain per message; any filter that ran before it would see the whole
/// batch as one request body.
/// </summary>
public class SqsStartupTests {

    private static SqsBatchFilter BuildFilter() {
        return new SqsBatchFilter(
            TestJson.Serializer,
            TestJson.Pool,
            new BatchProcessorExceptionHandler(),
            new NullMetricLoggerProvider(),
            NullLogger<SqsBatchFilter>.Instance);
    }

    [Fact]
    public async Task StartupRegistersTheBatchFilterGlobally() {
        var registry = Substitute.For<IGlobalFilterRegistry>();
        var filter = BuildFilter();

        var provider = new StubServiceProvider().Add(registry).Add(filter);

        Assert.True(await new SqsStartup().Startup(provider));

        registry.Received(1).RegisterFilter(filter, Arg.Any<int>());
    }

    /// <summary>
    /// Order -1 puts it ahead of the default (1000) and ahead of parameter binding, which is what
    /// gives each forked message its own bound handler arguments.
    /// </summary>
    [Fact]
    public async Task TheBatchFilterIsOrderedAheadOfEveryDefaultFilter() {
        var registry = Substitute.For<IGlobalFilterRegistry>();
        var filter = BuildFilter();

        await new SqsStartup().Startup(new StubServiceProvider().Add(registry).Add(filter));

        registry.Received(1).RegisterFilter(filter, -1);
    }

    [Fact]
    public async Task StartupFailsLoudlyWhenTheBatchFilterIsNotRegistered() {
        var provider = new StubServiceProvider().Add(Substitute.For<IGlobalFilterRegistry>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqsStartup().Startup(provider));
    }
}

/// <summary>
/// The module a consuming application applies with <c>[SqsLambda.Module]</c>.
/// </summary>
public class SqsLambdaTests {

    [Fact]
    public void TheModuleSuppliesTheDefaultBatchExceptionHandler() {
        var services = new ServiceCollection();

        new SqsLambda().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<BatchProcessorExceptionHandler>(provider.GetRequiredService<IBatchProcessorExceptionHandler>());
    }

    /// <summary>
    /// The handler is a singleton: it is consulted once per failing record, on a path that is
    /// already the slow one, and rebuilding it per record would allocate on every poison message.
    /// </summary>
    [Fact]
    public void TheBatchExceptionHandlerIsASingleton() {
        var services = new ServiceCollection();

        new SqsLambda().ConfigureServices(services);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IBatchProcessorExceptionHandler));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}

/// <summary>
/// The ambient context a handler can inject to see the message it is processing.
/// </summary>
public class SqsMessageContextTests {

    [Fact]
    public void AFreshContextCarriesNoEventAndNoMessage() {
        var context = new SqsMessageContext();

        Assert.Null(context.SqsEvent);
        Assert.Null(context.Message);
    }

    [Fact]
    public void TheEventAndMessageAreReadBackAsTheyWereSet() {
        var message = new SQSEvent.SQSMessage { MessageId = "m1" };
        var sqsEvent = new SQSEvent { Records = [message] };

        ISqsMessageContext context = new SqsMessageContext { SqsEvent = sqsEvent, Message = message };

        Assert.Same(sqsEvent, context.SqsEvent);
        Assert.Same(message, context.Message);
    }
}
