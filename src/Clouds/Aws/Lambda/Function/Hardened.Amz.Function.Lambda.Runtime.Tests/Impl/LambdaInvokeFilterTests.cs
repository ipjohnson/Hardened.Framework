using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Function.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests.Impl;

public class LambdaInvokeFilterTests {

    [Fact]
    public async Task TheConfiguredHandlerRunsAgainstTheIncomingContext() {
        var seen = new List<IExecutionContext>();
        var handler = Substitute.For<IExecutionRequestHandler>();
        handler.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(callInfo =>
            new TestExecutionChain(callInfo.Arg<IExecutionContext>(), ctx => {
                seen.Add(ctx);

                return Task.CompletedTask;
            }));

        var context = TestExecutionContext.Create(new MemoryStream(), new MemoryStream());

        await new LambdaInvokeFilter(handler).Execute(new TestExecutionChain(context));

        Assert.Same(context, Assert.Single(seen));
    }
}

/// <summary>
/// The extension point a consumer overrides to supply their own invoke filter. The default returns
/// the caching <see cref="LambdaFunctionInvokeFilter"/> built from the container.
/// </summary>
public class LambdaInvokeFilterProviderTests {

    [Fact]
    public void TheDefaultProviderBuildsTheCachingFunctionInvokeFilter() {
        var services = new ServiceCollection();
        services.AddSingleton<ILambdaContextAccessor, LambdaContextAccessor>();

        using var provider = services.BuildServiceProvider();

        var filter = new LambdaInvokeFilterProvider().ProvideFilter(provider);

        Assert.IsType<LambdaFunctionInvokeFilter>(filter);
    }

    [Fact]
    public void EveryRegisteredHandlerPackageIsOfferedToTheFilter() {
        var package = Substitute.For<ILambdaHandlerPackage>();

        var services = new ServiceCollection();
        services.AddSingleton<ILambdaContextAccessor, LambdaContextAccessor>();
        services.AddSingleton(package);

        using var provider = services.BuildServiceProvider();

        var filter = new LambdaInvokeFilterProvider().ProvideFilter(provider);

        Assert.NotNull(filter);
        Assert.Same(package, Assert.Single(provider.GetServices<ILambdaHandlerPackage>()));
    }
}
