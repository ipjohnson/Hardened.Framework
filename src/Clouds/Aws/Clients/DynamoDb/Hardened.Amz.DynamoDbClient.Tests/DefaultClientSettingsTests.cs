using System.Reflection;
using Amazon.Runtime;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Hardened.Amz.DynamoDbClient.Tests;

/// <summary>
/// The default client — the one every consumer gets until it configures something else, and the one
/// path nothing used to exercise.
///
/// <para>
/// There are two shapes, and the difference is not cosmetic. Deployed, the SDK resolves the region
/// and the role's credentials for itself. Pointed at DynamoDB Local, it must be told the endpoint
/// and handed credentials that mean nothing, because DynamoDB Local authenticates nothing but the
/// SDK still signs every request. Getting that branch wrong fails at the first call, not at startup.
/// </para>
/// </summary>
public class DefaultClientSettingsTests {

    private const string LocalUrl = "http://localhost:8000";

    /// <summary>
    /// The SDK normalises what it is handed, so a URL with no path comes back with a trailing
    /// slash. Asserted as it is stored rather than as it was written, since that is what a caller
    /// reading <c>Config.ServiceURL</c> will see.
    /// </summary>
    private const string AsTheSdkStoresIt = LocalUrl + "/";

    [Fact]
    public void WithNothingConfiguredEverythingIsLeftToTheSdk() {
        var (config, credentials) = Settings(new DynamoDbOptions());

        Assert.Null(credentials);
        Assert.Null(config.RegionEndpoint);
        Assert.Null(config.ServiceURL);
    }

    [Fact]
    public void ARegionIsParsedIntoAnEndpoint() {
        var (config, _) = Settings(new DynamoDbOptions { Region = "eu-west-1" });

        Assert.Equal("eu-west-1", config.RegionEndpoint.SystemName);
    }

    /// <summary>
    /// The region arrives from an environment variable, so an unset one shows up as empty rather
    /// than absent. That has to mean "the SDK decides", not "the region is the empty string".
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyRegionIsLeftToTheSdk(string region) {
        var (config, _) = Settings(new DynamoDbOptions { Region = region });

        Assert.Null(config.RegionEndpoint);
    }

    [Fact]
    public void AServiceUrlPointsTheClientAtIt() {
        var (config, _) = Settings(new DynamoDbOptions { ServiceUrl = LocalUrl });

        Assert.Equal(AsTheSdkStoresIt, config.ServiceURL);
    }

    /// <summary>
    /// The whole reason the branch exists. DynamoDB Local checks no credentials, but the SDK refuses
    /// to send a request it cannot sign, so credentials that mean nothing still have to be there.
    /// </summary>
    [Fact]
    public void AServiceUrlSuppliesCredentialsBecauseTheSdkSignsEveryRequest() {
        var (_, credentials) = Settings(new DynamoDbOptions { ServiceUrl = LocalUrl });

        Assert.IsType<BasicAWSCredentials>(credentials);
    }

    /// <summary>
    /// <c>RegionEndpoint</c> and <c>ServiceURL</c> are mutually exclusive on the SDK's config:
    /// assigning either clears the other. The region used to be assigned first and then wiped by the
    /// endpoint, so a process with both <c>AWS_REGION</c> and <c>DYNAMODB_SERVICE_URL</c> set — the
    /// ordinary local-development shape — silently signed as <c>us-east-1</c> whatever its region
    /// said. It is carried as the signing region instead.
    /// </summary>
    [Fact]
    public void ARegionGivenAlongsideAServiceUrlSurvivesAsTheSigningRegion() {
        var (config, credentials) = Settings(new DynamoDbOptions {
            ServiceUrl = LocalUrl,
            Region = "us-west-2",
        });

        Assert.Equal(AsTheSdkStoresIt, config.ServiceURL);
        Assert.Equal("us-west-2", config.AuthenticationRegion);
        Assert.NotNull(credentials);

        // Not an incidental detail — it is the reason the region has to go somewhere else.
        Assert.Null(config.RegionEndpoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyServiceUrlMeansDeployed(string serviceUrl) {
        var (config, credentials) = Settings(new DynamoDbOptions { ServiceUrl = serviceUrl, Region = "us-west-2" });

        Assert.Null(credentials);
        Assert.Null(config.ServiceURL);
    }

    /// <summary>
    /// The settings above are only worth anything if the default client is actually built from them,
    /// so this goes through the provider rather than the helper.
    /// </summary>
    [Fact]
    public void TheDefaultClientIsBuiltFromTheOptions() {
        var options = new DynamoDbOptions {
            ServiceUrl = LocalUrl,
            Region = "us-west-2",
        };

        var provider = new DynamoDbClientProvider(
            Options.Create<IDynamoDbOptions>(options),
            Substitute.For<IServiceProvider>());

        var client = provider.GetClient();

        Assert.Equal(AsTheSdkStoresIt, client.Config.ServiceURL);
        Assert.Equal("us-west-2", client.Config.AuthenticationRegion);
    }

    /// <summary>
    /// These two names are this package's contract with the environment it is deployed into, and
    /// nothing else in this repository mentions them: the code that reads them is generated at the
    /// consuming application's entry point, so renaming one here compiles, packs and publishes
    /// cleanly, and then silently stops configuring anything.
    /// </summary>
    [Fact]
    public void TheEnvironmentVariableNamesAreFixed() {
        Assert.Equal("DYNAMODB_SERVICE_URL", EnvironmentVariableBehind("_serviceUrl"));
        Assert.Equal("AWS_REGION", EnvironmentVariableBehind("_region"));
    }

    private static (Amazon.DynamoDBv2.AmazonDynamoDBConfig Config, AWSCredentials? Credentials) Settings(
        DynamoDbOptions options) =>
        DynamoDbClientProvider.DefaultClientSettings(options);

    private static string? EnvironmentVariableBehind(string fieldName) =>
        typeof(DynamoDbOptions)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetCustomAttribute<FromEnvironmentVariableAttribute>()
            ?.EnvironmentVariable;
}
