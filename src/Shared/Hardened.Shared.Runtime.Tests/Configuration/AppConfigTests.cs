using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Configuration;

/// <summary>
/// <see cref="AppConfig"/> — the builder an application contributes so it can change a model a
/// library defined without editing the library.
///
/// <para>
/// Asserted through a real <see cref="ConfigurationManager"/> wherever the observable outcome is
/// "what the application ends up resolving", because that is what an amender exists to change.
/// The behaviours come from <c>Hardened.Docs/website/guide/configuration.md</c>, "Amending
/// configuration".
/// </para>
/// </summary>
public class AppConfigTests {

    public interface IRetryConfiguration {
        int MaxAttempts { get; }
    }

    public class RetryConfiguration : IRetryConfiguration {
        public int MaxAttempts { get; set; } = 1;

        public List<string> Applied { get; } = [];
    }

    private static EnvironmentImpl Environment(string name = "development") => new(name);

    private static IRetryConfiguration Resolve(AppConfig config, IHardenedEnvironment? environment = null) {
        environment ??= Environment();

        return new ConfigurationManager(environment, [config]).GetConfiguration<IRetryConfiguration>();
    }

    private static AppConfig WithDefaultRetry() {
        var config = new AppConfig();

        config.ProvideValue<IRetryConfiguration, RetryConfiguration>(_ => new RetryConfiguration());

        return config;
    }

    /// <summary>
    /// Documented: "<c>ProvideValue</c> supplies the implementation rather than amending the
    /// default."
    /// </summary>
    [Fact]
    public void ProvideValueSuppliesTheImplementation() {
        var config = new AppConfig();

        config.ProvideValue<IRetryConfiguration, RetryConfiguration>(_ => new RetryConfiguration { MaxAttempts = 9 });

        Assert.Equal(9, Resolve(config).MaxAttempts);
    }

    /// <summary>The value provider is handed the environment, so the value can depend on it.</summary>
    [Fact]
    public void ProvideValueReceivesTheEnvironment() {
        var config = new AppConfig();

        config.ProvideValue<IRetryConfiguration, RetryConfiguration>(
            environment => new RetryConfiguration { MaxAttempts = environment.Matches("production") ? 5 : 1 });

        Assert.Equal(5, Resolve(config, Environment("production")).MaxAttempts);
        Assert.Equal(1, Resolve(config, Environment("development")).MaxAttempts);
    }

    [Fact]
    public void AnAmenderChangesTheModelTheApplicationResolves() {
        var config = WithDefaultRetry();

        config.Amend((RetryConfiguration retry) => retry.MaxAttempts = 5);

        Assert.Equal(5, Resolve(config).MaxAttempts);
    }

    /// <summary>
    /// Documented: "all of them run, in registration order, the first time the model is resolved."
    /// Order is the whole point — the last amender is the one whose value survives.
    /// </summary>
    [Fact]
    public void AmendersRunInRegistrationOrder() {
        var config = WithDefaultRetry();

        config.Amend((RetryConfiguration retry) => retry.Applied.Add("first"));
        config.Amend((RetryConfiguration retry) => retry.Applied.Add("second"));
        config.Amend((RetryConfiguration retry) => retry.Applied.Add("third"));

        var resolved = (RetryConfiguration)Resolve(config);

        Assert.Equal(["first", "second", "third"], resolved.Applied);
    }

    [Fact]
    public void TheLastAmenderToSetAValueWins() {
        var config = WithDefaultRetry();

        config.Amend((RetryConfiguration retry) => retry.MaxAttempts = 2);
        config.Amend((RetryConfiguration retry) => retry.MaxAttempts = 7);

        Assert.Equal(7, Resolve(config).MaxAttempts);
    }

    /// <summary>
    /// Documented: "Amenders run against the concrete model — <c>DynamoDbOptions</c>, not
    /// <c>IDynamoDbOptions</c> — because amending is the one place that is allowed to write."
    /// </summary>
    [Fact]
    public void AnAmenderForAnUnrelatedTypeLeavesTheModelAlone() {
        var config = WithDefaultRetry();

        config.Amend((RetryConfiguration retry) => retry.MaxAttempts = 5);
        config.Amend((UnrelatedConfiguration unrelated) => unrelated.Touched = true);

        Assert.Equal(5, Resolve(config).MaxAttempts);
    }

    /// <summary>
    /// Documented: "<c>Amend</c> takes an environment name. Passing one restricts the amender to that
    /// environment." This is how a local-development endpoint override stays out of production.
    /// </summary>
    [Fact]
    public void AnEnvironmentScopedAmenderRunsInThatEnvironment() {
        var config = WithDefaultRetry();

        config.Amend((RetryConfiguration retry) => retry.MaxAttempts = 5, "development");

        Assert.Equal(5, Resolve(config, Environment("development")).MaxAttempts);
    }

    [Fact]
    public void AnEnvironmentScopedAmenderDoesNotRunInAnotherEnvironment() {
        var config = WithDefaultRetry();

        config.Amend((RetryConfiguration retry) => retry.MaxAttempts = 5, "development");

        Assert.Equal(1, Resolve(config, Environment("production")).MaxAttempts);
    }

    /// <summary>An amender with no environment named runs everywhere, which is the default.</summary>
    [Theory]
    [InlineData("development")]
    [InlineData("staging")]
    [InlineData("production")]
    public void AnUnscopedAmenderRunsInEveryEnvironment(string environment) {
        var config = WithDefaultRetry();

        config.Amend((RetryConfiguration retry) => retry.MaxAttempts = 5);

        Assert.Equal(5, Resolve(config, Environment(environment)).MaxAttempts);
    }

    /// <summary>
    /// Scoped and unscoped amenders coexist, and the scoped one still runs in registration order
    /// relative to the rest rather than before or after all of them.
    /// </summary>
    [Fact]
    public void AScopedAmenderKeepsItsPlaceInRegistrationOrder() {
        var config = WithDefaultRetry();

        config.Amend((RetryConfiguration retry) => retry.Applied.Add("unscoped-first"));
        config.Amend((RetryConfiguration retry) => retry.Applied.Add("scoped"), "development");
        config.Amend((RetryConfiguration retry) => retry.Applied.Add("unscoped-last"));

        var resolved = (RetryConfiguration)Resolve(config, Environment("development"));

        Assert.Equal(["unscoped-first", "scoped", "unscoped-last"], resolved.Applied);
    }

    [Fact]
    public void AScopedAmenderIsSkippedWithoutDisturbingTheOrderOfTheRest() {
        var config = WithDefaultRetry();

        config.Amend((RetryConfiguration retry) => retry.Applied.Add("unscoped-first"));
        config.Amend((RetryConfiguration retry) => retry.Applied.Add("scoped"), "development");
        config.Amend((RetryConfiguration retry) => retry.Applied.Add("unscoped-last"));

        var resolved = (RetryConfiguration)Resolve(config, Environment("production"));

        Assert.Equal(["unscoped-first", "unscoped-last"], resolved.Applied);
    }

    /// <summary>
    /// Documented: "The overload taking a function receives the environment, for when the value
    /// itself depends on it."
    /// </summary>
    [Fact]
    public void TheFunctionOverloadReceivesTheEnvironment() {
        var config = WithDefaultRetry();

        config.Amend((IHardenedEnvironment environment, RetryConfiguration retry) => {
            retry.MaxAttempts = environment.Matches("production") ? 5 : 1;
            return retry;
        });

        Assert.Equal(5, Resolve(config, Environment("production")).MaxAttempts);
        Assert.Equal(1, Resolve(config, Environment("development")).MaxAttempts);
    }

    /// <summary>
    /// The function overload takes its place in the same ordered list as the action overload; they
    /// are not two separate chains.
    /// </summary>
    [Fact]
    public void BothAmendOverloadsShareOneOrderedChain() {
        var config = WithDefaultRetry();

        config.Amend((RetryConfiguration retry) => retry.Applied.Add("action"));
        config.Amend((IHardenedEnvironment _, RetryConfiguration retry) => {
            retry.Applied.Add("function");
            return retry;
        });

        var resolved = (RetryConfiguration)Resolve(config);

        Assert.Equal(["action", "function"], resolved.Applied);
    }

    /// <summary>
    /// Every registered package contributes its amenders, so an application can amend a model a
    /// library provided without either knowing about the other.
    /// </summary>
    [Fact]
    public void EveryRegisteredPackageContributesItsAmenders() {
        var library = new AppConfig();
        var application = new AppConfig();

        library.ProvideValue<IRetryConfiguration, RetryConfiguration>(_ => new RetryConfiguration());
        library.Amend((RetryConfiguration retry) => retry.Applied.Add("library"));
        application.Amend((RetryConfiguration retry) => retry.Applied.Add("application"));

        var resolved = (RetryConfiguration)new ConfigurationManager(Environment(), [library, application])
            .GetConfiguration<IRetryConfiguration>();

        Assert.Equal(["library", "application"], resolved.Applied);
    }

    /// <summary>
    /// A later package's value provider replaces an earlier one for the same interface, which is how
    /// an application overrides a library's default outright.
    /// </summary>
    [Fact]
    public void ALaterPackageReplacesAnEarlierProviderForTheSameInterface() {
        var library = new AppConfig();
        var application = new AppConfig();

        library.ProvideValue<IRetryConfiguration, RetryConfiguration>(_ => new RetryConfiguration { MaxAttempts = 1 });
        application.ProvideValue<IRetryConfiguration, RetryConfiguration>(_ => new RetryConfiguration { MaxAttempts = 9 });

        var resolved = new ConfigurationManager(Environment(), [library, application])
            .GetConfiguration<IRetryConfiguration>();

        Assert.Equal(9, resolved.MaxAttempts);
    }

    /// <summary>Every builder method returns the same instance, so calls chain.</summary>
    [Fact]
    public void TheBuilderMethodsChain() {
        var config = new AppConfig();

        var chained = config
            .ProvideValue<IRetryConfiguration, RetryConfiguration>(_ => new RetryConfiguration())
            .Amend((RetryConfiguration retry) => retry.MaxAttempts = 2)
            .Amend((RetryConfiguration retry) => retry.MaxAttempts = 3, "development")
            .Amend((IHardenedEnvironment _, RetryConfiguration retry) => retry);

        Assert.Same(config, chained);
    }

    /// <summary>An empty package contributes nothing and breaks nothing.</summary>
    [Fact]
    public void AnEmptyAppConfigContributesNothing() {
        IConfigurationPackage config = new AppConfig();
        var environment = Environment();

        Assert.Empty(config.ConfigurationValueProviders(environment));
        Assert.Empty(config.ConfigurationValueAmenders(environment));
    }

    public class UnrelatedConfiguration {
        public bool Touched { get; set; }
    }
}
