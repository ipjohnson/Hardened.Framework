using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using NSubstitute;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Configuration;

/// <summary>
/// The three pieces <see cref="ConfigurationManager"/> is assembled from: the two value providers,
/// the package that carries them, and the amender that rewrites what they produced.
/// </summary>
public class ConfigurationValueProviderTests {

    public interface IServiceOptions {
        string ServiceUrl { get; }
    }

    public class ServiceOptions : IServiceOptions {
        public string ServiceUrl { get; set; } = "http://default";
    }

    private static EnvironmentImpl Environment(string name = "development") => new(name);

    private static void NoAmenders(IHardenedEnvironment environment, object value) { }

    /// <summary>
    /// The provider names the interface it satisfies and the type it builds. Registration is keyed on
    /// the interface, so getting this wrong makes the model unresolvable rather than wrong.
    /// </summary>
    [Fact]
    public void ANewProviderNamesTheInterfaceAndTheImplementation() {
        var provider = new NewConfigurationValueProvider<IServiceOptions, ServiceOptions>(null);

        Assert.Equal(typeof(IServiceOptions), provider.InterfaceType);
        Assert.Equal(typeof(ServiceOptions), provider.ImplementationType);
    }

    [Fact]
    public void ANewProviderConstructsTheImplementation() {
        var provider = new NewConfigurationValueProvider<IServiceOptions, ServiceOptions>(null);

        Assert.IsType<ServiceOptions>(provider.ProvideValue(Environment(), NoAmenders));
    }

    /// <summary>
    /// A provider with no init action is the shape the generator emits for a model with no
    /// environment-backed field, and it must not be treated as a missing collaborator.
    /// </summary>
    [Fact]
    public void ANewProviderWithNoInitActionLeavesTheDefaultsAlone() {
        var provider = new NewConfigurationValueProvider<IServiceOptions, ServiceOptions>(null);

        var value = (ServiceOptions)provider.ProvideValue(Environment(), NoAmenders);

        Assert.Equal("http://default", value.ServiceUrl);
    }

    [Fact]
    public void ANewProviderRunsItsInitActionBeforeReturning() {
        var provider = new NewConfigurationValueProvider<IServiceOptions, ServiceOptions>(
            (_, options) => options.ServiceUrl = "http://initialised");

        var value = (ServiceOptions)provider.ProvideValue(Environment(), NoAmenders);

        Assert.Equal("http://initialised", value.ServiceUrl);
    }

    [Fact]
    public void ANewProviderPassesTheEnvironmentToItsInitAction() {
        IHardenedEnvironment? received = null;
        var environment = Environment("staging");

        var provider = new NewConfigurationValueProvider<IServiceOptions, ServiceOptions>(
            (given, _) => received = given);

        provider.ProvideValue(environment, NoAmenders);

        Assert.Same(environment, received);
    }

    /// <summary>
    /// The init action runs before the amenders, so an application amending a model overrides what
    /// the environment set rather than being overwritten by it.
    /// </summary>
    [Fact]
    public void ANewProviderRunsItsInitActionBeforeTheAmenders() {
        var order = new List<string>();

        var provider = new NewConfigurationValueProvider<IServiceOptions, ServiceOptions>(
            (_, _) => order.Add("init"));

        provider.ProvideValue(Environment(), (_, _) => order.Add("amend"));

        Assert.Equal(["init", "amend"], order);
    }

    [Fact]
    public void AFuncProviderNamesTheInterfaceAndTheImplementation() {
        var provider = new FuncConfigurationValueProvider<IServiceOptions, ServiceOptions>(_ => new ServiceOptions());

        Assert.Equal(typeof(IServiceOptions), provider.InterfaceType);
        Assert.Equal(typeof(ServiceOptions), provider.ImplementationType);
    }

    [Fact]
    public void AFuncProviderReturnsWhatItsFunctionBuilt() {
        var built = new ServiceOptions { ServiceUrl = "http://built" };
        var provider = new FuncConfigurationValueProvider<IServiceOptions, ServiceOptions>(_ => built);

        Assert.Same(built, provider.ProvideValue(Environment(), NoAmenders));
    }

    [Fact]
    public void AFuncProviderPassesTheEnvironmentToItsFunction() {
        IHardenedEnvironment? received = null;
        var environment = Environment("staging");

        var provider = new FuncConfigurationValueProvider<IServiceOptions, ServiceOptions>(given => {
            received = given;
            return new ServiceOptions();
        });

        provider.ProvideValue(environment, NoAmenders);

        Assert.Same(environment, received);
    }

    [Fact]
    public void AFuncProviderAmendsWhatItsFunctionBuilt() {
        var built = new ServiceOptions();
        object? amended = null;

        var provider = new FuncConfigurationValueProvider<IServiceOptions, ServiceOptions>(_ => built);

        provider.ProvideValue(Environment(), (_, value) => amended = value);

        Assert.Same(built, amended);
    }

    /// <summary>
    /// An amender only applies to the model it was written for. A configuration package holds
    /// amenders for every type, and every one of them is offered every value.
    /// </summary>
    [Fact]
    public void AnAmenderAppliesToItsOwnType() {
        var amender = new SimpleConfigurationValueAmender<ServiceOptions>((_, options) => {
            options.ServiceUrl = "http://amended";
            return options;
        });

        var value = new ServiceOptions();

        Assert.Same(value, amender.ApplyConfiguration(Environment(), value));
        Assert.Equal("http://amended", value.ServiceUrl);
    }

    [Fact]
    public void AnAmenderLeavesAValueOfAnotherTypeUntouched() {
        var ran = false;

        var amender = new SimpleConfigurationValueAmender<ServiceOptions>((_, options) => {
            ran = true;
            return options;
        });

        var unrelated = new object();

        Assert.Same(unrelated, amender.ApplyConfiguration(Environment(), unrelated));
        Assert.False(ran);
    }

    [Fact]
    public void AnAmenderNamesTheTypeItAmends() {
        var amender = new SimpleConfigurationValueAmender<ServiceOptions>((_, options) => options);

        Assert.Equal(typeof(ServiceOptions), amender.ConfigurationType);
    }

    [Fact]
    public void AnAmenderReceivesTheEnvironment() {
        IHardenedEnvironment? received = null;
        var environment = Environment("staging");

        var amender = new SimpleConfigurationValueAmender<ServiceOptions>((given, options) => {
            received = given;
            return options;
        });

        amender.ApplyConfiguration(environment, new ServiceOptions());

        Assert.Same(environment, received);
    }

    /// <summary>
    /// A package built from providers alone reports no amenders rather than null, so a manager
    /// iterating them does not have to check.
    /// </summary>
    [Fact]
    public void APackageOfProvidersAloneReportsNoAmenders() {
        var provider = new NewConfigurationValueProvider<IServiceOptions, ServiceOptions>(null);
        var package = new SimpleConfigurationPackage([provider]);
        var environment = Environment();

        Assert.Same(provider, Assert.Single(package.ConfigurationValueProviders(environment)));
        Assert.Empty(package.ConfigurationValueAmenders(environment));
    }

    [Fact]
    public void APackageCarriesBothItsProvidersAndItsAmenders() {
        var provider = new NewConfigurationValueProvider<IServiceOptions, ServiceOptions>(null);
        var amender = Substitute.For<IConfigurationValueAmender>();

        var package = new SimpleConfigurationPackage([provider], [amender]);
        var environment = Environment();

        Assert.Same(provider, Assert.Single(package.ConfigurationValueProviders(environment)));
        Assert.Same(amender, Assert.Single(package.ConfigurationValueAmenders(environment)));
    }

    /// <summary>
    /// A simple package reports the same contents whatever environment it is asked about — the
    /// environment-sensitive package is <see cref="AppConfig"/>.
    /// </summary>
    [Theory]
    [InlineData("development")]
    [InlineData("production")]
    public void APackageReportsTheSameContentsInEveryEnvironment(string environment) {
        var provider = new NewConfigurationValueProvider<IServiceOptions, ServiceOptions>(null);
        var package = new SimpleConfigurationPackage([provider]);

        Assert.Same(provider, Assert.Single(package.ConfigurationValueProviders(Environment(environment))));
    }
}
