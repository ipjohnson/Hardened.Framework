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

    /// <summary>
    /// The message names the type that could not be resolved. Documented as the signal that "the
    /// model lives in an assembly whose module was never imported", which is unguessable from a
    /// message that only says a configuration was missing.
    /// </summary>
    [Fact]
    public void TheUnregisteredTypeMessageNamesTheTypeAndWhatIsWrong() {
        var manager = new ConfigurationManager(
            Substitute.For<IHardenedEnvironment>(), Array.Empty<IConfigurationPackage>());

        var exception = Assert.Throws<Exception>(() => manager.GetConfiguration<ITestConfig>());

        Assert.Contains(nameof(ITestConfig), exception.Message);
        Assert.Contains("not a registered configuration type", exception.Message);
    }

    /// <summary>
    /// Registering the implementation does not register the interface. A caller asking for the model
    /// type gets the same "not registered" failure as one asking for something that does not exist,
    /// because registration is keyed on <c>InterfaceType</c>.
    /// </summary>
    [Fact]
    public void RegisteringAnInterfaceDoesNotAlsoRegisterTheImplementation() {
        var env = Substitute.For<IHardenedEnvironment>();
        var provider = new NewConfigurationValueProvider<ITestConfig, TestConfig>(null);
        var manager = new ConfigurationManager(env, new[] { new SimpleConfigurationPackage(new[] { provider }) });

        Assert.NotNull(manager.GetConfiguration<ITestConfig>());
        Assert.Throws<Exception>(() => manager.GetConfiguration<TestConfig>());
    }

    /// <summary>
    /// Amenders are collected across packages in registration order, so a library's own amender runs
    /// before an application's override of it.
    /// </summary>
    [Fact]
    public void AmendersFromEveryPackageRunInPackageOrder() {
        var env = Substitute.For<IHardenedEnvironment>();
        var applied = new List<string>();

        var package1 = new SimpleConfigurationPackage(
            new IConfigurationValueProvider[] { new NewConfigurationValueProvider<ITestConfig, TestConfig>(null) },
            new IConfigurationValueAmender[] { new RecordingAmender(applied, "first") });

        var package2 = new SimpleConfigurationPackage(
            Array.Empty<IConfigurationValueProvider>(),
            new IConfigurationValueAmender[] { new RecordingAmender(applied, "second") });

        new ConfigurationManager(env, new[] { package1, package2 }).GetConfiguration<ITestConfig>();

        Assert.Equal(new[] { "first", "second" }, applied);
    }

    /// <summary>
    /// Amenders run once per type, not once per resolution. Running them again on the cached value
    /// would double every list an amender appends to.
    /// </summary>
    [Fact]
    public void AmendersRunOnceEvenWhenTheConfigurationIsResolvedRepeatedly() {
        var env = Substitute.For<IHardenedEnvironment>();
        var applied = new List<string>();

        var package = new SimpleConfigurationPackage(
            new IConfigurationValueProvider[] { new NewConfigurationValueProvider<ITestConfig, TestConfig>(null) },
            new IConfigurationValueAmender[] { new RecordingAmender(applied, "amender") });

        var manager = new ConfigurationManager(env, new[] { package });

        manager.GetConfiguration<ITestConfig>();
        manager.GetConfiguration<ITestConfig>();
        manager.GetConfiguration<ITestConfig>();

        Assert.Equal(new[] { "amender" }, applied);
    }

    /// <summary>
    /// Every amender is offered every value, whatever type it was written for — filtering is the
    /// amender's job, not the manager's.
    /// </summary>
    [Fact]
    public void EveryAmenderIsOfferedEveryConfigurationValue() {
        var env = Substitute.For<IHardenedEnvironment>();
        var applied = new List<string>();

        var package = new SimpleConfigurationPackage(
            new IConfigurationValueProvider[] {
                new NewConfigurationValueProvider<ITestConfig, TestConfig>(null),
                new NewConfigurationValueProvider<IOtherConfig, OtherConfig>(null)
            },
            new IConfigurationValueAmender[] { new RecordingAmender(applied, "amender") });

        var manager = new ConfigurationManager(env, new[] { package });

        manager.GetConfiguration<ITestConfig>();
        manager.GetConfiguration<IOtherConfig>();

        Assert.Equal(new[] { "amender", "amender" }, applied);
    }

    /// <summary>
    /// A manager built from no packages at all resolves nothing, rather than failing while it is
    /// being constructed.
    /// </summary>
    [Fact]
    public void AManagerWithNoPackagesConstructsAndResolvesNothing() {
        var manager = new ConfigurationManager(
            Substitute.For<IHardenedEnvironment>(), Array.Empty<IConfigurationPackage>());

        Assert.Throws<Exception>(() => manager.GetConfiguration<ITestConfig>());
    }

    /// <summary>
    /// Once a value is cached, concurrent readers all get the one instance. Configuration is read
    /// from every request path, so this is the access pattern that matters at run time.
    /// </summary>
    [Fact]
    public async Task ACachedConfigurationIsTheSameInstanceForEveryConcurrentReader() {
        var env = Substitute.For<IHardenedEnvironment>();

        var manager = new ConfigurationManager(env, new[] {
            new SimpleConfigurationPackage(
                new IConfigurationValueProvider[] { new NewConfigurationValueProvider<ITestConfig, TestConfig>(null) })
        });

        var expected = manager.GetConfiguration<ITestConfig>();

        var resolved = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(() => manager.GetConfiguration<ITestConfig>())));

        Assert.All(resolved, value => Assert.Same(expected, value));
    }

    private class RecordingAmender(List<string> applied, string name) : IConfigurationValueAmender {
        public object ApplyConfiguration(IHardenedEnvironment environment, object configurationValue) {
            lock (applied) {
                applied.Add(name);
            }

            return configurationValue;
        }
    }
}
