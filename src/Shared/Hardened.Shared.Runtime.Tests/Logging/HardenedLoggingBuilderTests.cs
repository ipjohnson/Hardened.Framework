using Hardened.Shared.Runtime.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Logging;

/// <summary>
/// The <see cref="ILoggingBuilder"/> a Hardened application hands to the standard logging
/// extension methods.
/// </summary>
/// <remarks>
/// It carries nothing but the service collection, and that is the point: every
/// <c>AddConsole</c>-shaped extension in the BCL is written against <see cref="ILoggingBuilder"/>
/// and registers into <see cref="ILoggingBuilder.Services"/>. Handing back a different collection
/// than the one the application is being built into would make those calls silently no-ops.
/// </remarks>
public class HardenedLoggingBuilderTests {

    [Fact]
    public void ServicesIsTheCollectionItWasGiven() {
        var services = new ServiceCollection();

        Assert.Same(services, new HardenedLoggingBuilder(services).Services);
    }

    /// <summary>
    /// A standard logging extension registers into the application's own collection.
    /// </summary>
    [Fact]
    public void AStandardLoggingExtensionRegistersIntoTheApplicationsCollection() {
        var services = new ServiceCollection();

        ((ILoggingBuilder)new HardenedLoggingBuilder(services)).AddFilter("Test", LogLevel.Warning);

        Assert.NotEmpty(services);
    }
}
