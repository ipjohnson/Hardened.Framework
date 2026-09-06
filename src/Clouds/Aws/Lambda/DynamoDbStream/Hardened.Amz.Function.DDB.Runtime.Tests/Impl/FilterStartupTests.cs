using Hardened.Amz.Function.DDB.Runtime.Impl;
using Hardened.Amz.Function.DDB.Runtime.Tests.Infrastructure;
using Hardened.Amz.Function.Lambda.Runtime.Filter;
using Hardened.Requests.Abstract.RequestFilter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Amz.Function.DDB.Runtime.Tests.Impl;

/// <summary>
/// The stream filter has to be registered globally and ahead of everything else: it deserialises
/// the stream envelope and forks a chain per record, so any filter running before it would see the
/// whole batch as one request.
/// </summary>
public class FilterStartupTests {

    private static DynamoDbExecutionFilter BuildFilter() {
        return new DynamoDbExecutionFilter(
            TestJson.Serializer,
            TestJson.Pool,
            new BatchProcessorExceptionHandler(),
            new CurrentDdbRecordContext(),
            new NullMetricLoggerProvider(),
            NullLogger<DynamoDbExecutionFilter>.Instance);
    }

    [Fact]
    public async Task StartupRegistersTheStreamFilterGlobally() {
        var registry = Substitute.For<IGlobalFilterRegistry>();
        var filter = BuildFilter();

        Assert.True(await new FilterStartup().Startup(new StubServiceProvider().Add(registry).Add(filter)));

        registry.Received(1).RegisterFilter(filter, Arg.Any<int>());
    }

    [Fact]
    public async Task TheStreamFilterIsOrderedAheadOfEveryDefaultFilter() {
        var registry = Substitute.For<IGlobalFilterRegistry>();
        var filter = BuildFilter();

        await new FilterStartup().Startup(new StubServiceProvider().Add(registry).Add(filter));

        registry.Received(1).RegisterFilter(filter, -1);
    }

    [Fact]
    public async Task StartupFailsLoudlyWhenTheStreamFilterIsNotRegistered() {
        var provider = new StubServiceProvider().Add(Substitute.For<IGlobalFilterRegistry>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => new FilterStartup().Startup(provider));
    }
}

/// <summary>
/// The module a consuming application applies with <c>[DynamoStreamLambda.Module]</c>.
/// </summary>
public class DynamoStreamLambdaTests {

    [Fact]
    public void TheModuleSuppliesTheDefaultBatchExceptionHandler() {
        var services = new ServiceCollection();

        new DynamoStreamLambda().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<BatchProcessorExceptionHandler>(
            provider.GetRequiredService<IBatchProcessorExceptionHandler>());
    }

    [Fact]
    public void TheBatchExceptionHandlerIsASingleton() {
        var services = new ServiceCollection();

        new DynamoStreamLambda().ConfigureServices(services);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IBatchProcessorExceptionHandler));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
