using Hardened.Aws.Lambda.Runtime.Streaming;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Aws.Lambda.Runtime.Tests.Streaming;

/// <summary>
/// The deployment setting that says which wire protocol the front door expects. Read once, at
/// startup, and wrong in exactly one way.
/// </summary>
public class LambdaResponseModeConfigurationTests {

    [Theory]
    [InlineData(null, LambdaResponseMode.Buffered)]
    [InlineData("", LambdaResponseMode.Buffered)]
    [InlineData("   ", LambdaResponseMode.Buffered)]
    [InlineData("buffered", LambdaResponseMode.Buffered)]
    [InlineData("Buffered", LambdaResponseMode.Buffered)]
    [InlineData("stream", LambdaResponseMode.Stream)]
    [InlineData("STREAM", LambdaResponseMode.Stream)]
    [InlineData(" stream ", LambdaResponseMode.Stream)]
    public void TheSettingParsesCaseInsensitivelyWithBufferedAsTheDefault(string? value, LambdaResponseMode expected) {
        Assert.Equal(expected, LambdaResponseModeConfiguration.Parse(value));
    }

    /// <summary>
    /// A value that is neither fails rather than falling back. A misspelt setting would otherwise
    /// run buffered behind a front door expecting the prelude, and every response would be a 500
    /// with nothing in the logs to say why.
    /// </summary>
    [Theory]
    [InlineData("streaming")]
    [InlineData("RESPONSE_STREAM")]
    [InlineData("true")]
    public void AnUnrecognisedValueFailsNamingTheVariableAndTheChoices(string value) {
        var failure = Assert.Throws<InvalidOperationException>(() => LambdaResponseModeConfiguration.Parse(value));

        Assert.Contains(LambdaResponseModeConfiguration.EnvironmentVariable, failure.Message);
        Assert.Contains(value, failure.Message);
        Assert.Contains("'buffered'", failure.Message);
        Assert.Contains("'stream'", failure.Message);
    }

    /// <summary>
    /// What the CDK writes is what the application reads, by construction rather than by two
    /// string literals that happen to agree.
    /// </summary>
    [Theory]
    [InlineData(LambdaResponseMode.Buffered)]
    [InlineData(LambdaResponseMode.Stream)]
    public void TheWrittenValueParsesBackToTheMode(LambdaResponseMode mode) {
        Assert.Equal(mode, LambdaResponseModeConfiguration.Parse(LambdaResponseModeConfiguration.ValueOf(mode)));
    }

    [Fact]
    public void TheEnvironmentVariableSetsTheMode() {
        var environment = new EnvironmentImpl(environmentValues: new Dictionary<string, string> {
            [LambdaResponseModeConfiguration.EnvironmentVariable] = "stream"
        });
        var configuration = new LambdaResponseModeConfiguration();

        LambdaResponseModeConfiguration.FromEnvironment(environment, configuration);

        Assert.Equal(LambdaResponseMode.Stream, configuration.Mode);
    }

    [Fact]
    public void AnEnvironmentWithoutTheVariableIsBuffered() {
        var configuration = new LambdaResponseModeConfiguration();

        LambdaResponseModeConfiguration.FromEnvironment(
            new EnvironmentImpl(environmentValues: new Dictionary<string, string>()), configuration);

        Assert.Equal(LambdaResponseMode.Buffered, configuration.Mode);
    }

    [Fact]
    public void AFreshConfigurationIsBuffered() {
        Assert.Equal(LambdaResponseMode.Buffered, new LambdaResponseModeConfiguration().Mode);
    }

    /// <summary>
    /// The application's amendment runs after the variable has been read, and wins over it.
    /// </summary>
    /// <remarks>
    /// That order is what makes <c>ConfigureLambdaResponseMode</c> worth having: an application only
    /// ever deployed behind a streaming function URL can say so in code rather than depending on a
    /// variable being set correctly in every stack that creates it.
    /// </remarks>
    [Fact]
    public void ConfigureLambdaResponseModeWinsOverTheEnvironment() {
        var services = new ServiceCollection();

        services.ConfigureLambdaResponseMode(mode => mode.Mode = LambdaResponseMode.Stream);

        var environment = new EnvironmentImpl(environmentValues: new Dictionary<string, string> {
            [LambdaResponseModeConfiguration.EnvironmentVariable] = "buffered"
        });

        // The provider the runtime module registers, then whatever the extension added beside it.
        IConfigurationPackage[] packages = [
            new SimpleConfigurationPackage(new IConfigurationValueProvider[] {
                new NewConfigurationValueProvider<ILambdaResponseModeConfiguration, LambdaResponseModeConfiguration>(
                    LambdaResponseModeConfiguration.FromEnvironment)
            }),
            .. services.BuildServiceProvider().GetServices<IConfigurationPackage>()
        ];

        var manager = new ConfigurationManager(environment, packages);

        Assert.Equal(
            LambdaResponseMode.Stream,
            manager.GetConfiguration<ILambdaResponseModeConfiguration>().Mode);
    }
}
