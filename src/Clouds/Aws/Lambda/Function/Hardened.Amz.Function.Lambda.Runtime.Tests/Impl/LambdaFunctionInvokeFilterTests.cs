using Amazon.Lambda.Core;
using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Function.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using NSubstitute;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests.Impl;

/// <summary>
/// Resolution of a Lambda handler by function name. Every function app in this repository reaches
/// its handler through this filter, and it does so once per cold start — the handler is cached
/// deliberately.
/// </summary>
public class LambdaFunctionInvokeFilterTests {

    private static ILambdaHandlerPackage PackageReturning(IExecutionRequestHandler? handler) {
        var package = Substitute.For<ILambdaHandlerPackage>();
        package.GetFunctionHandler(Arg.Any<IServiceProvider>(), Arg.Any<ILambdaContext>()).Returns(handler);

        return package;
    }

    private static (IExecutionRequestHandler Handler, List<IExecutionContext> Invocations) RecordingHandler() {
        var invocations = new List<IExecutionContext>();
        var handler = Substitute.For<IExecutionRequestHandler>();

        handler.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(callInfo => {
            var context = callInfo.Arg<IExecutionContext>();

            return new TestExecutionChain(context, ctx => {
                invocations.Add(ctx);

                return Task.CompletedTask;
            });
        });

        return (handler, invocations);
    }

    private static ILambdaContextAccessor AccessorFor(string functionName) {
        return new LambdaContextAccessor { Context = new FakeLambdaContext(functionName) };
    }

    private static IExecutionChain ChainWith(IServiceProvider rootServiceProvider) {
        using var body = new MemoryStream();
        using var responseBody = new MemoryStream();

        return new TestExecutionChain(TestExecutionContext.Create(body, responseBody, rootServiceProvider));
    }

    [Fact]
    public async Task TheHandlerFromTheFirstPackageThatClaimsTheFunctionRunsTheRequest() {
        var (claimed, invocations) = RecordingHandler();
        var (unwanted, unwantedInvocations) = RecordingHandler();

        var filter = new LambdaFunctionInvokeFilter(
            AccessorFor("Process"),
            new[] { PackageReturning(null), PackageReturning(claimed), PackageReturning(unwanted) });

        await filter.Execute(ChainWith(new StubServiceProvider()));

        Assert.Single(invocations);
        Assert.Empty(unwantedInvocations);
    }

    /// <summary>
    /// A function name no package recognises has to name the function it could not find — the whole
    /// message a developer gets from a mis-wired handler is this string.
    /// </summary>
    [Fact]
    public async Task AnUnknownFunctionNameThrowsNamingTheFunction() {
        var filter = new LambdaFunctionInvokeFilter(
            AccessorFor("NoSuchFunction"),
            new[] { PackageReturning(null), PackageReturning(null) });

        var exception = await Assert.ThrowsAsync<Exception>(
            () => filter.Execute(ChainWith(new StubServiceProvider())));

        Assert.Contains("NoSuchFunction", exception.Message);
    }

    [Fact]
    public async Task NoPackagesAtAllThrowsRatherThanReturningSilently() {
        var filter = new LambdaFunctionInvokeFilter(
            AccessorFor("Process"), Array.Empty<ILambdaHandlerPackage>());

        await Assert.ThrowsAsync<Exception>(() => filter.Execute(ChainWith(new StubServiceProvider())));
    }

    /// <summary>
    /// The filter documents the handler as cached for performance. A second invocation must not go
    /// back to the packages — on a warm Lambda that lookup would run on every request.
    /// </summary>
    [Fact]
    public async Task TheResolvedHandlerIsLookedUpOnceAndReusedAfterwards() {
        var (handler, invocations) = RecordingHandler();
        var package = PackageReturning(handler);

        var filter = new LambdaFunctionInvokeFilter(AccessorFor("Process"), new[] { package });

        await filter.Execute(ChainWith(new StubServiceProvider()));
        await filter.Execute(ChainWith(new StubServiceProvider()));

        Assert.Equal(2, invocations.Count);
        package.Received(1).GetFunctionHandler(Arg.Any<IServiceProvider>(), Arg.Any<ILambdaContext>());
    }

    /// <summary>
    /// Packages select a handler from the <see cref="ILambdaContext"/>, so the context the accessor
    /// is holding is the one they must be offered.
    /// </summary>
    [Fact]
    public async Task PackagesAreOfferedTheContextHeldByTheAccessor() {
        var (handler, _) = RecordingHandler();
        var package = PackageReturning(handler);
        var accessor = AccessorFor("Process");

        await new LambdaFunctionInvokeFilter(accessor, new[] { package })
            .Execute(ChainWith(new StubServiceProvider()));

        package.Received(1).GetFunctionHandler(Arg.Any<IServiceProvider>(), accessor.Context!);
    }

    /// <summary>
    /// Packages are handed the root provider, not the request scope: a handler is cached for the
    /// life of the process and would otherwise capture services from whichever request happened to
    /// arrive first.
    /// </summary>
    [Fact]
    public async Task PackagesAreOfferedTheRootServiceProvider() {
        var (handler, _) = RecordingHandler();
        var package = PackageReturning(handler);
        var rootProvider = new StubServiceProvider();

        await new LambdaFunctionInvokeFilter(AccessorFor("Process"), new[] { package })
            .Execute(ChainWith(rootProvider));

        package.Received(1).GetFunctionHandler(rootProvider, Arg.Any<ILambdaContext>());
    }
}
