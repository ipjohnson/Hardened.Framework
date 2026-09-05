using Hardened.Requests.Abstract.Compression;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Web.Runtime.Compression;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Compression;

/// <summary>
/// The two attribute forms, the predicate factory they reach, and the application-wide default
/// that stands down for a handler carrying either.
/// </summary>
public class CompressAttributeTests {

    private class Controller { }

    private static ExecutionRequestHandlerInfo Handler(params object[] metadata) =>
        new("/pets", "GET", typeof(Controller), "List", metadata: metadata);

    /// <summary>Refuses anything but one integer, so a test can see the arguments arrived.</summary>
    private sealed class OneInteger : ICompressionPredicate {
        public static ICompressionPredicate Create(object[] args) => args is [int]
            ? new OneInteger()
            : throw new ArgumentException("OneInteger takes one integer.");

        public bool ShouldCompress(object value, IExecutionContext context) => true;
    }

    [Fact]
    public void ThePlainFormInstallsOneFilterOutsideTheResponseCache() {
        var info = Assert.Single(new CompressAttribute().GetFilters(Handler()));

        Assert.Equal(FilterOrder.Before + FilterOrder.ResponseCache, info.Order);
        Assert.IsType<ResponseCompressionFilter>(info.FilterFunc(null!));
    }

    /// <summary>
    /// One instance per handler, shared by every request, the way the cache filter is.
    /// </summary>
    [Fact]
    public void TheFilterIsBuiltOncePerHandler() {
        var info = Assert.Single(new CompressAttribute().GetFilters(Handler()));

        Assert.Same(info.FilterFunc(null!), info.FilterFunc(null!));
    }

    [Fact]
    public void TheGenericFormHandsTheArgumentsToThePredicate() {
        var info = Assert.Single(new CompressAttribute<OneInteger>(50).GetFilters(Handler()));

        Assert.IsType<ResponseCompressionFilter>(info.FilterFunc(null!));
    }

    /// <summary>
    /// The build cannot check that the arguments match what the predicate expects, so the factory
    /// does, as the chain is built, naming the handler.
    /// </summary>
    [Fact]
    public void APredicateRefusingItsArgumentsFailsNamingTheHandler() {
        var attribute = new CompressAttribute<OneInteger>("fifty");

        var exception = Assert.Throws<InvalidOperationException>(() => attribute.GetFilters(Handler()).ToList());

        Assert.Contains("GET /pets", exception.Message);
        Assert.Contains("OneInteger", exception.Message);
        Assert.Contains("one integer", exception.Message);
    }

    [Fact]
    public void FavorIsCarriedByBothForms() {
        Assert.Equal(CompressionType.Br, new CompressAttribute { Favor = CompressionType.Br }.Favor);
        Assert.Equal(CompressionType.Br, new CompressAttribute<OneInteger>(1) { Favor = CompressionType.Br }.Favor);
    }

    [Fact]
    public void AHandlerDeclaresCompressionInEitherForm() {
        Assert.True(CompressAttribute.Declares(Handler(new CompressAttribute())));
        Assert.True(CompressAttribute.Declares(Handler(new object(), new CompressAttribute<OneInteger>(1))));
        Assert.False(CompressAttribute.Declares(Handler(new object())));
        Assert.False(CompressAttribute.Declares(Handler()));
    }

    /// <summary>
    /// The module installs the default as a provider that yields nothing for a handler carrying
    /// its own declaration, so explicit beats convention without the registration saying so.
    /// </summary>
    [Fact]
    public void TheModuleDefaultStandsDownForAHandlerThatDeclaresItsOwn() {
        var services = new ServiceCollection();

        new ResponseCompression().ConfigureServices(services);

        var provider = Assert.Single(services.BuildServiceProvider().GetServices<IRequestFilterProvider>());

        Assert.Single(provider.GetFilters(Handler()));
        Assert.Empty(provider.GetFilters(Handler(new CompressAttribute())));
        Assert.Empty(provider.GetFilters(Handler(new CompressAttribute<OneInteger>(1) { Favor = CompressionType.Br })));
    }

    [Fact]
    public void EveryInstallOfTheModuleIsTheSameInstall() {
        Assert.Equal(new ResponseCompression(), new ResponseCompression());
        Assert.Equal(new ResponseCompression().GetHashCode(), new ResponseCompression().GetHashCode());
    }
}
