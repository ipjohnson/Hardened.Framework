using Hardened.Amz.Web.Lambda.Runtime;
using Xunit;

namespace Hardened.Amz.Web.Lambda.SourceGenerator.Tests;

/// <summary>
/// <c>[LambdaWebApplication(Version = ...)]</c> and the two <see cref="ProxyIntegrationType"/> values.
///
/// <para>
/// API Gateway has two payload formats. The REST integration sends
/// <c>APIGatewayProxyRequest</c>; the HTTP API v2 integration sends
/// <c>APIGatewayHttpApiV2ProxyRequest</c>, which is a different shape with a different context, a
/// different route key and cookies in their own array. A handler bound to one cannot deserialise the
/// other.
/// </para>
///
/// <para>
/// <b>The generator does not distinguish them.</b> <c>LambdaWebApplicationAttribute.Version</c> is
/// declared, is settable, and is read by nothing: <see cref="WebLambdaSourceGenerator"/> selects on
/// <c>[HardenedModule]</c> and never looks at the attribute, and
/// <see cref="LambdaWebApplicationFileWriter"/> hard-codes the v2 request and response types.
/// Recorded 2026-08-12 and reported rather than asserted as intended — see
/// <c>docs/testing-conventions.md</c> §6. The tests below pin what actually happens, so that
/// implementing the option is visible as a change here.
/// </para>
/// </summary>
public class ProxyIntegrationTypeTests {

    private static string ApplicationFor(string attributes) =>
        WebGeneratorHarness.Generate(WebGeneratorHarness.Application(attributes: attributes))
            .SourceContaining("App");

    /// <summary>
    /// Both values of the enum, and no attribute at all, produce byte-identical applications.
    /// </summary>
    [Theory]
    [InlineData("[LambdaWebApplication(Version = ProxyIntegrationType.ApiGateway)]")]
    [InlineData("[LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]")]
    [InlineData("")]
    public void TheProxyIntegrationVersionDoesNotChangeWhatIsEmitted(string attribute) {
        Assert.Equal(ApplicationFor(""), ApplicationFor(attribute));
    }

    /// <summary>
    /// What every application gets regardless: the HTTP API v2 payload types. An application
    /// deployed behind a REST API integration is handed a <c>APIGatewayProxyRequest</c> payload that
    /// this signature cannot bind, and fails at the runtime's deserialisation rather than at build.
    /// </summary>
    [Fact]
    public void EveryApplicationIsEmittedForTheHttpApiV2PayloadFormat() {
        var application = ApplicationFor(
            "[LambdaWebApplication(Version = ProxyIntegrationType.ApiGateway)]");

        Assert.Contains("APIGatewayHttpApiV2ProxyRequest", application);
        Assert.Contains("APIGatewayHttpApiV2ProxyResponse", application);
        Assert.DoesNotContain("global::Amazon.Lambda.APIGatewayEvents.APIGatewayProxyRequest ", application);
    }

    /// <summary>
    /// The attribute is not what selects the generator either — the generator selects on
    /// <c>[HardenedModule]</c>, so an application carrying <c>[LambdaWebApplication]</c> and nothing
    /// else gets no file.
    /// </summary>
    [Fact]
    public void TheLambdaWebApplicationAttributeAloneDoesNotClaimAnEntryPoint() {
        var result = WebGeneratorHarness.Generate("""
            using Hardened.Amz.Web.Lambda.Runtime;

            namespace TestApp;

            [LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]
            public partial class Application {
            }
            """);

        Assert.Empty(result.GeneratedSources);
    }

    /// <summary>
    /// The enum a consumer would set. Both values exist and are distinct, which is what makes the
    /// generator ignoring them a gap rather than a non-feature.
    /// </summary>
    [Fact]
    public void BothProxyIntegrationTypesAreDeclaredAndDistinct() {
        Assert.NotEqual(ProxyIntegrationType.ApiGateway, ProxyIntegrationType.HttpApiV2);
    }
}
