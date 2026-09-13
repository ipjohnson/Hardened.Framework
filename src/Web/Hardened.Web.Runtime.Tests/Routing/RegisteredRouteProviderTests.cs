using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Web.Runtime.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Routing;

/// <summary>
/// What the provider answers before and after registration closes.
/// </summary>
/// <remarks>
/// The window this covers is short in every supported host, because
/// <c>ApplicationLogic.RunApplication</c> awaits every startup service before the host is handed
/// the delegate that starts serving. It is covered because 404 and 503 are cached differently by
/// everything in front of an application, and answering the wrong one during a restart is the kind
/// of thing that outlives the restart.
/// </remarks>
public class RegisteredRouteProviderTests
{
    [Fact]
    public void AnApplicationWithNoRegistrationIsReadyBeforeStartupRuns()
    {
        var provider = new RegisteredRouteProvider(Services(), expectsRegistrations: false);

        Assert.True(provider.IsReady);
        Assert.Null(provider.GetExecutionRequestHandler(Context("/orders")));
    }

    [Fact]
    public void APublishedTableAnswers()
    {
        var services = Services();
        var provider = new RegisteredRouteProvider(services, expectsRegistrations: true);

        provider.Publish(Table());

        var matched = provider.GetExecutionRequestHandler(Context("/orders/7"));

        Assert.NotNull(matched);
        Assert.Equal("7", matched!.PathTokens.Get("id").ToString());
    }

    [Fact]
    public void APathTheTableDoesNotHaveFallsThrough()
    {
        var provider = new RegisteredRouteProvider(Services(), expectsRegistrations: true);

        provider.Publish(Table());

        Assert.Null(provider.GetExecutionRequestHandler(Context("/elsewhere")));
    }

    [Fact]
    public async Task ARequestBeforeRegistrationClosesIsAnswered503()
    {
        var services = Services();
        var provider = new RegisteredRouteProvider(services, expectsRegistrations: true);

        Assert.False(provider.IsReady);

        var context = Context("/orders/7", services);
        var matched = provider.GetExecutionRequestHandler(context);

        Assert.NotNull(matched);

        await matched!.Handler!.GetExecutionChain(context).Next();

        Assert.Equal(503, context.Response.Status);
        Assert.Equal("1", context.Response.Headers[KnownHeaders.RetryAfter].ToString());
        Assert.False(context.Response.ShouldSerialize);
    }

    [Fact]
    public void ThePendingAnswerIsBuiltOnce()
    {
        var services = Services();
        var provider = new RegisteredRouteProvider(services, expectsRegistrations: true);

        Assert.Same(
            provider.GetExecutionRequestHandler(Context("/one", services))!.Handler,
            provider.GetExecutionRequestHandler(Context("/two", services))!.Handler
        );
    }

    // ---- helpers ------------------------------------------------------------

    private static RuntimeRouteTable Table()
    {
        var builder = new RuntimeRouteTableBuilder();

        Assert.True(
            builder.TryAdd(
                "/orders/{id:int}",
                "GET",
                _ => Substitute.For<IExecutionRequestHandler>(),
                out var error
            ),
            error
        );

        return builder.Build();
    }

    /// <summary>
    /// The services <c>ExecutionHelper</c> resolves while assembling a chain, on the same terms
    /// <c>HealthCheckProviderTests</c> sets them up.
    /// </summary>
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();

        var ioProvider = Substitute.For<IIOFilterProvider>();

        ioProvider
            .ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>()
            )
            .Returns(new PassThrough());

        services.AddSingleton(ioProvider);
        services.AddSingleton<IInstanceFilterProvider, InstanceFilterProvider>();
        services.AddSingleton<IGlobalFilterRegistry>(
            new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>())
        );
        services.AddSingleton<RouteRegistrationController>();

        return services.BuildServiceProvider();
    }

    private sealed class PassThrough : IExecutionFilter
    {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    private static IExecutionContext Context(string path, IServiceProvider? services = null)
    {
        services ??= Services();

        return new TestExecutionContext(
            services,
            services,
            Substitute.For<IKnownServices>(),
            new TestExecutionRequest(
                "GET",
                path,
                "application/json",
                new SimpleQueryStringCollection(new Dictionary<string, string>())
            ),
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None
        );
    }
}
