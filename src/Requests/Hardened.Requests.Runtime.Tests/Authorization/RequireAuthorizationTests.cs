using System.Reflection;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Authorization;

/// <summary>
/// The runtime half of <c>[RequireAuthorization]</c>: the registration the generator emits, and what
/// it does to the configuration the filter provider reads.
/// </summary>
/// <remarks>
/// The diagnostic is the visible half and only covers what a generator compiled. This is the half
/// that guards a handler arriving from a referenced assembly nothing analysed, so it is the one that
/// has to be right.
/// </remarks>
public class RequireAuthorizationTests {

    private static readonly IHardenedEnvironment Environment = Substitute.For<IHardenedEnvironment>();

    /// <summary>The provider the framework registers by default, with the posture off.</summary>
    private static IConfigurationPackage Defaults() =>
        new SimpleConfigurationPackage(new IConfigurationValueProvider[] {
            new NewConfigurationValueProvider<IAuthorizationConfiguration, AuthorizationConfiguration>(null)
        });

    private static IConfigurationPackage Amendment() {
        var services = new ServiceCollection();

        services.RequireAuthorization();

        return services.BuildServiceProvider().GetRequiredService<IConfigurationPackage>();
    }

    #region the posture

    /// <summary>
    /// Nothing said, nothing guarded. Existing applications are unaffected, which is the first rung
    /// of the ladder.
    /// </summary>
    [Fact]
    public void TheDefaultIsThatNothingIsRequired() {
        var manager = new ConfigurationManager(Environment, new[] { Defaults() });

        Assert.False(manager.GetConfiguration<IAuthorizationConfiguration>().RequireAuthorization);
    }

    [Fact]
    public void RequireAuthorizationTurnsThePostureOn() {
        var manager = new ConfigurationManager(Environment, new[] { Defaults(), Amendment() });

        Assert.True(manager.GetConfiguration<IAuthorizationConfiguration>().RequireAuthorization);
    }

    /// <summary>
    /// The reason it is an amender rather than a replacement provider. Generated code registers into
    /// the same collection as the module that registers the default, and nothing orders the two -
    /// so a mechanism that depended on running second would work or not depending on which
    /// registration happened to be enumerated first.
    /// </summary>
    [Fact]
    public void ItHoldsWhicheverOrderTheRegistrationsHappenIn() {
        var afterwards = new ConfigurationManager(Environment, new[] { Defaults(), Amendment() });
        var beforehand = new ConfigurationManager(Environment, new[] { Amendment(), Defaults() });

        Assert.True(afterwards.GetConfiguration<IAuthorizationConfiguration>().RequireAuthorization);
        Assert.True(beforehand.GetConfiguration<IAuthorizationConfiguration>().RequireAuthorization);
    }

    #endregion

    #region the amender itself

    /// <summary>
    /// An amender is handed every configuration value the application builds, not only the one it
    /// cares about, so it has to leave the rest alone.
    /// </summary>
    [Fact]
    public void TheAmenderLeavesOtherConfigurationUntouched() {
        var amender = Assert.Single(Amendment().ConfigurationValueAmenders(Environment));
        var unrelated = new UnrelatedConfiguration { Value = "original" };

        var returned = amender.ApplyConfiguration(Environment, unrelated);

        Assert.Same(unrelated, returned);
        Assert.Equal("original", unrelated.Value);
    }

    [Fact]
    public void TheAmenderReturnsTheValueItWasGiven() {
        var amender = Assert.Single(Amendment().ConfigurationValueAmenders(Environment));
        var configuration = new AuthorizationConfiguration();

        Assert.Same(configuration, amender.ApplyConfiguration(Environment, configuration));
    }

    /// <summary>
    /// It contributes no provider of its own, so it cannot displace the default one and lose any
    /// other amendment applied to the same configuration.
    /// </summary>
    [Fact]
    public void TheRegistrationAddsNoValueProvider() {
        Assert.Empty(Amendment().ConfigurationValueProviders(Environment));
    }

    private class UnrelatedConfiguration {
        public string Value { get; set; } = "";
    }

    #endregion

    #region the attribute

    /// <summary>
    /// Class or assembly, so it works when the module class lives in another project.
    /// <c>[BasePath]</c> already supports both for the same reason.
    /// </summary>
    [Fact]
    public void TheAttributeAppliesToAClassOrAnAssembly() {
        var usage = typeof(RequireAuthorizationAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Class | AttributeTargets.Assembly, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    #endregion
}
