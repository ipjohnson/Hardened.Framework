using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Amz.Shared.Lambda.Runtime.Tests;

/// <summary>
/// What a consumer sees when they forget the module attribute.
///
/// <para>
/// The generated bootstrap resolves its transport's services in the application's constructor, so
/// this is the first thing that happens when the module is missing — before a request is served, and
/// on a deployed function, on its first invocation. The message is the whole value of the type, so
/// it is asserted rather than left to <c>Assert.Throws</c> alone.
/// </para>
/// </summary>
public class ApplicationServicesTests {
    private interface IHandlerService;

    private class HandlerService : IHandlerService;

    private static IServiceProvider ProviderWith(params Action<IServiceCollection>[] registrations) {
        var services = new ServiceCollection();

        foreach (var registration in registrations) {
            registration(services);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Require_ReturnsTheRegisteredService() {
        var provider = ProviderWith(services => services.AddSingleton<IHandlerService, HandlerService>());

        var resolved = ApplicationServices.Require<IHandlerService>(provider, "OrderApp", "LambdaWebModule");

        Assert.Same(provider.GetRequiredService<IHandlerService>(), resolved);
    }

    /// <summary>
    /// The four things the message has to carry: which application, which attribute to add, which
    /// service went missing, and where the attribute belongs. The attribute name appears in the
    /// instruction as well as the diagnosis, because that is the sentence a reader acts on.
    /// </summary>
    [Fact]
    public void Require_NamesTheApplicationAndTheMissingModule() {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ApplicationServices.Require<IHandlerService>(
                ProviderWith(), "OrderApp", "LambdaWebModule"));

        Assert.Contains("'OrderApp'", exception.Message);
        Assert.Contains("[LambdaWebModule]", exception.Message);
        Assert.Contains(typeof(IHandlerService).FullName!, exception.Message);
        Assert.Contains("[HardenedModule]", exception.Message);
    }

    /// <summary>
    /// The message is built from its arguments, not from a per-transport string. A streaming
    /// application that named the buffered module would send the reader to the wrong attribute.
    /// </summary>
    [Fact]
    public void Require_NamesWhicheverModuleItWasGiven() {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ApplicationServices.Require<IHandlerService>(
                ProviderWith(), "StreamingApp", "StreamingLambdaWebModule"));

        Assert.Contains("[StreamingLambdaWebModule]", exception.Message);
        Assert.DoesNotContain("[LambdaWebModule]", exception.Message.Replace("[StreamingLambdaWebModule]", ""));
    }

    /// <summary>
    /// A registration that resolves to null is the same failure as no registration at all, and has
    /// to produce the same message rather than a NullReferenceException further along.
    /// </summary>
    [Fact]
    public void Require_TreatsANullRegistrationAsMissing() {
        var provider = ProviderWith(services => services.AddSingleton<IHandlerService>(_ => null!));

        var exception = Assert.Throws<InvalidOperationException>(
            () => ApplicationServices.Require<IHandlerService>(provider, "OrderApp", "LambdaWebModule"));

        Assert.Contains("[LambdaWebModule]", exception.Message);
    }
}
