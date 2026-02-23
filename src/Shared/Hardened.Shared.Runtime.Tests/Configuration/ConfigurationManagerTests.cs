using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using NSubstitute;
using SimpleFixture.NSubstitute;
using SimpleFixture.xUnit;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Configuration;

[SubFixtureInitialize]
public class ConfigurationManagerTests {
    public interface ITestConfig { }

    public class TestConfig : ITestConfig { }

    public interface IOtherConfig { }

    public class OtherConfig : IOtherConfig { }

    [Fact]
    public void GetConfiguration_ReturnsValueFromProvider() {
        var env = Substitute.For<IHardenedEnvironment>();
        var config = new TestConfig();

        var provider = Substitute.For<IConfigurationValueProvider>();
        provider.InterfaceType.Returns(typeof(ITestConfig));
        provider.ProvideValue(env, Arg.Any<Action<IHardenedEnvironment, object>>()).Returns(config);

        var package = Substitute.For<IConfigurationPackage>();
        package.ConfigurationValueProviders(env).Returns(new[] { provider });
        package.ConfigurationValueAmenders(env).Returns(Array.Empty<IConfigurationValueAmender>());

        var manager = new ConfigurationManager(env, new[] { package });

        var result = manager.GetConfiguration<ITestConfig>();

        Assert.Same(config, result);
    }

    [Fact]
    public void GetConfiguration_CachesResultOnSecondCall() {
        var env = Substitute.For<IHardenedEnvironment>();
        var config = new TestConfig();

        var provider = Substitute.For<IConfigurationValueProvider>();
        provider.InterfaceType.Returns(typeof(ITestConfig));
        provider.ProvideValue(env, Arg.Any<Action<IHardenedEnvironment, object>>()).Returns(config);

        var package = Substitute.For<IConfigurationPackage>();
        package.ConfigurationValueProviders(env).Returns(new[] { provider });
        package.ConfigurationValueAmenders(env).Returns(Array.Empty<IConfigurationValueAmender>());

        var manager = new ConfigurationManager(env, new[] { package });

        var first = manager.GetConfiguration<ITestConfig>();
        var second = manager.GetConfiguration<ITestConfig>();

        Assert.Same(first, second);
        provider.Received(1).ProvideValue(env, Arg.Any<Action<IHardenedEnvironment, object>>());
    }

    [Fact]
    public void GetConfiguration_ThrowsForUnregisteredType() {
        var env = Substitute.For<IHardenedEnvironment>();

        var package = Substitute.For<IConfigurationPackage>();
        package.ConfigurationValueProviders(env).Returns(Array.Empty<IConfigurationValueProvider>());
        package.ConfigurationValueAmenders(env).Returns(Array.Empty<IConfigurationValueAmender>());

        var manager = new ConfigurationManager(env, new[] { package });

        var ex = Assert.Throws<Exception>(() => manager.GetConfiguration<ITestConfig>());
        Assert.Contains("ITestConfig", ex.Message);
    }

    [Fact]
    public void Amenders_AreAppliedToConfigurationValues() {
        var env = Substitute.For<IHardenedEnvironment>();
        var config = new TestConfig();

        var provider = Substitute.For<IConfigurationValueProvider>();
        provider.InterfaceType.Returns(typeof(ITestConfig));
        provider.ProvideValue(env, Arg.Any<Action<IHardenedEnvironment, object>>())
            .Returns(ci => {
                var amender = ci.Arg<Action<IHardenedEnvironment, object>>();
                amender(env, config);
                return config;
            });

        var valueAmender = Substitute.For<IConfigurationValueAmender>();
        valueAmender.ApplyConfiguration(env, config).Returns(config);

        var package = Substitute.For<IConfigurationPackage>();
        package.ConfigurationValueProviders(env).Returns(new[] { provider });
        package.ConfigurationValueAmenders(env).Returns(new[] { valueAmender });

        var manager = new ConfigurationManager(env, new[] { package });

        manager.GetConfiguration<ITestConfig>();

        valueAmender.Received(1).ApplyConfiguration(env, config);
    }

    [Fact]
    public void MultiplePackages_RegisterProvidersCorrectly() {
        var env = Substitute.For<IHardenedEnvironment>();
        var testConfig = new TestConfig();
        var otherConfig = new OtherConfig();

        var provider1 = Substitute.For<IConfigurationValueProvider>();
        provider1.InterfaceType.Returns(typeof(ITestConfig));
        provider1.ProvideValue(env, Arg.Any<Action<IHardenedEnvironment, object>>()).Returns(testConfig);

        var provider2 = Substitute.For<IConfigurationValueProvider>();
        provider2.InterfaceType.Returns(typeof(IOtherConfig));
        provider2.ProvideValue(env, Arg.Any<Action<IHardenedEnvironment, object>>()).Returns(otherConfig);

        var package1 = Substitute.For<IConfigurationPackage>();
        package1.ConfigurationValueProviders(env).Returns(new[] { provider1 });
        package1.ConfigurationValueAmenders(env).Returns(Array.Empty<IConfigurationValueAmender>());

        var package2 = Substitute.For<IConfigurationPackage>();
        package2.ConfigurationValueProviders(env).Returns(new[] { provider2 });
        package2.ConfigurationValueAmenders(env).Returns(Array.Empty<IConfigurationValueAmender>());

        var manager = new ConfigurationManager(env, new[] { package1, package2 });

        Assert.Same(testConfig, manager.GetConfiguration<ITestConfig>());
        Assert.Same(otherConfig, manager.GetConfiguration<IOtherConfig>());
    }
}
