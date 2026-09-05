using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Web.Runtime.Conditional;
using Hardened.Web.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Conditional;

/// <summary>
/// How the filter reaches a handler: the attribute on an operation or a class, the
/// application-wide default that <c>[Enable&lt;ConditionalGet&gt;]</c> installs and that stands
/// down for a handler carrying the attribute, and nothing at all otherwise.
/// </summary>
public class ConditionalGetAttributeTests {

    private class Controller { }

    private static ExecutionRequestHandlerInfo Handler(string method, params object[] metadata) =>
        new("/rates", method, typeof(Controller), "Read", metadata: metadata);

    /// <summary>A handler the generator marked as streaming, which the flag is the only sign of.</summary>
    private static ExecutionRequestHandlerInfo Streaming(string method, params object[] metadata) =>
        new("/feed", method, typeof(Controller), "Feed", metadata: metadata, streamsResponse: true);

    /// <summary>
    /// A HEAD reaches the GET handler through the routing table, so a handler declared for GET is
    /// the one a HEAD is revalidated at.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("get")]
    [InlineData("HEAD")]
    public void TheAttributeInstallsOneFilterAtTheConditionalStageOfAReadHandler(string method) {
        var info = Assert.Single(new ConditionalGetAttribute().GetFilters(Handler(method)));

        Assert.Equal(FilterOrder.Conditional, info.Order);
        Assert.IsType<ConditionalGetFilter>(info.FilterFunc(null!));
    }

    /// <summary>
    /// The conditionals mean a 412 on a write, which is not what this filter answers, so a
    /// class-level declaration on a controller that also writes installs nothing on the writes.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void AWriteHandlerGetsNoFilter(string method) {
        Assert.Empty(new ConditionalGetAttribute().GetFilters(Handler(method)));
    }

    /// <summary>One instance per declaration, shared by every request, the way the cache filter is.</summary>
    [Fact]
    public void TheFilterIsBuiltOncePerDeclaration() {
        var attribute = new ConditionalGetAttribute();

        var first = Assert.Single(attribute.GetFilters(Handler("GET"))).FilterFunc(null!);
        var second = Assert.Single(attribute.GetFilters(Handler("GET"))).FilterFunc(null!);

        Assert.Same(first, second);
    }

    [Fact]
    public void AHandlerDeclaresConditionalGetOnTheMethodOrTheClass() {
        Assert.True(ConditionalGetAttribute.Declares(Handler("GET", new ConditionalGetAttribute())));
        Assert.True(ConditionalGetAttribute.Declares(Handler("GET", new object(), new ConditionalGetAttribute())));
        Assert.False(ConditionalGetAttribute.Declares(Handler("GET", new object())));
        Assert.False(ConditionalGetAttribute.Declares(Handler("GET")));
    }

    /// <summary>
    /// The module installs the default as a provider that yields nothing for a handler carrying
    /// its own declaration, so explicit beats convention without the registration saying so, and
    /// nothing for a write.
    /// </summary>
    [Fact]
    public void TheModuleDefaultCoversEveryReadHandlerThatDoesNotDeclareItsOwn() {
        var services = new ServiceCollection();

        new ConditionalGet().ConfigureServices(services);

        var provider = Assert.Single(services.BuildServiceProvider().GetServices<IRequestFilterProvider>());

        Assert.Single(provider.GetFilters(Handler("GET")));
        Assert.Empty(provider.GetFilters(Handler("GET", new ConditionalGetAttribute())));
        Assert.Empty(provider.GetFilters(Handler("POST")));
    }

    /// <summary>
    /// Tagging a response means holding it back until it is all there, and a stream held back is
    /// not a stream: enabled application-wide, this arrived a server-sent event feed as one packet
    /// after the last event, with an ETag on it. So the default stands down for a handler that
    /// streams, and so does a declaration reaching one from its class.
    /// </summary>
    [Fact]
    public void AStreamingHandlerGetsNothingFromTheModuleDefault() {
        var services = new ServiceCollection();

        new ConditionalGet().ConfigureServices(services);

        var provider = Assert.Single(services.BuildServiceProvider().GetServices<IRequestFilterProvider>());

        Assert.Empty(provider.GetFilters(Streaming("GET")));
        Assert.Single(provider.GetFilters(Handler("GET")));
    }

    [Fact]
    public void AStreamingHandlerGetsNothingFromTheAttribute() {
        Assert.Empty(new ConditionalGetAttribute().GetFilters(Streaming("GET")));
        Assert.Empty(new ConditionalGetAttribute().GetFilters(Streaming("HEAD")));
    }

    [Fact]
    public void EveryInstallOfTheModuleIsTheSameInstall() {
        Assert.Equal(new ConditionalGet(), new ConditionalGet());
        Assert.Equal(new ConditionalGet().GetHashCode(), new ConditionalGet().GetHashCode());
        Assert.NotEqual<object>(new ConditionalGet(), new object());
    }

    /// <summary>
    /// The decision this feature was reworked around: a service that declares nothing carries
    /// none of it, so it pays nothing for it.
    /// </summary>
    [Fact]
    public void TheWebModuleInstallsNothingWithoutADeclaration() {
        var services = new ServiceCollection();

        new HardenedWebModule().ConfigureServices(services);

        Assert.Empty(services.BuildServiceProvider().GetServices<IRequestFilterProvider>());
    }
}
