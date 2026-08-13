using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Web.Kestrel.Runtime.Impl;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests.Impl;

/// <summary>
/// The context, and mostly <c>Clone</c>.
///
/// A filter forks the chain by cloning the context with something replaced — a rebound request,
/// a redirected response — and running the rest of the chain against the copy. Everything not
/// replaced has to carry over, or the fork silently loses whatever was dropped.
/// </summary>
public class FeatureExecutionContextTests {

    private static FeatureExecutionContext Context(ServerFeatures features, out IServiceProvider provider) {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IKnownServices>());
        provider = services.BuildServiceProvider();

        return new FeatureExecutionContext(
            provider, provider, features.Collection, Substitute.For<IMetricLogger>());
    }

    [Fact]
    public void Constructor_ReadsTheRequestAndResponseFromTheFeatures() {
        var features = new ServerFeatures("PUT", "/things/7");

        var context = Context(features, out var provider);

        Assert.Equal("PUT", context.Request.Method);
        Assert.Equal("/things/7", context.Request.Path);
        Assert.Same(provider, context.RootServiceProvider);
        Assert.NotNull(context.KnownServices);
    }

    /// <summary>A null argument keeps the current value — that is what makes a partial fork work.</summary>
    [Fact]
    public void Clone_KeepsEverythingWhenNothingIsReplaced() {
        var context = Context(new ServerFeatures(), out _);
        context.HandlerInstance = "handler";
        context.HandlerInfo = Substitute.For<IExecutionRequestHandlerInfo>();

        var clone = context.Clone(null, null, null, null);

        Assert.Same(context.Request, clone.Request);
        Assert.Same(context.Response, clone.Response);
        Assert.Same(context.RequestServices, clone.RequestServices);
        Assert.Same(context.RequestMetrics, clone.RequestMetrics);
        Assert.Same(context.KnownServices, clone.KnownServices);
        Assert.Equal(context.StartTime, clone.StartTime);
        Assert.Equal("handler", clone.HandlerInstance);
        Assert.Same(context.HandlerInfo, clone.HandlerInfo);
    }

    [Fact]
    public void Clone_ReplacesOnlyWhatItIsGiven() {
        var context = Context(new ServerFeatures(), out _);
        var request = Substitute.For<IExecutionRequest>();
        var metrics = Substitute.For<IMetricLogger>();

        var clone = context.Clone(request, null, null, metrics);

        Assert.Same(request, clone.Request);
        Assert.Same(metrics, clone.RequestMetrics);
        Assert.Same(context.Response, clone.Response);
    }

    /// <summary>
    /// Completion belongs to the body feature the server supplied, so a clone that replaced the
    /// response still completes the real one. Otherwise a request finished through a fork leaves
    /// the connection waiting.
    /// </summary>
    [Fact]
    public async Task Clone_StillCompletesTheServerResponse() {
        var features = new ServerFeatures();
        var context = Context(features, out _);

        var clone = (FeatureExecutionContext)context.Clone(
            null, Substitute.For<IExecutionResponse>(), null, null);

        await clone.CompleteAsync();

        Assert.Equal(1, features.ResponseBody.CompleteCount);
    }

    [Fact]
    public void Constructor_ThrowsWhenTheServerOmitsARequiredFeature() {
        var features = new ServerFeatures();
        features.Collection.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(null!);

        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IKnownServices>());
        var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => new FeatureExecutionContext(
            provider, provider, features.Collection, Substitute.For<IMetricLogger>()));
    }
}
