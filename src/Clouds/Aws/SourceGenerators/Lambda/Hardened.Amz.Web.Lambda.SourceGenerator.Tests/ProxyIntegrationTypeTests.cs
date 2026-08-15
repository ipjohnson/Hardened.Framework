using Hardened.Amz.Web.Lambda.Runtime;
using Microsoft.CodeAnalysis;
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
/// Only the v2 format is implemented. <c>Version</c> used to be declared, settable and read by
/// nothing — recorded on 2026-08-12 and reported rather than asserted as intended, per
/// <c>docs/testing-conventions.md</c> §6 — so selecting the REST integration produced a v2 handler
/// that a REST API would feed a v1 payload, and the function failed at the runtime's
/// deserialisation in a deployed environment. Since 2026-08-15 it is a build error (HRDAWS001).
/// The tests below are the ones that were reporting the gap, turned into assertions now that there
/// is a defined behaviour to assert.
/// </para>
/// </summary>
public class ProxyIntegrationTypeTests {

    private static string ApplicationFor(string attributes) =>
        WebGeneratorHarness.Generate(WebGeneratorHarness.Application(attributes: attributes))
            .SourceContaining("App");

    /// <summary>
    /// The value that works, and no attribute at all, produce byte-identical applications — v2 is
    /// the default, so saying so changes nothing.
    /// </summary>
    [Theory]
    [InlineData("[LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]")]
    [InlineData("[LambdaWebApplication]")]
    [InlineData("")]
    public void TheHttpApiV2VersionIsWhatIsEmittedWithOrWithoutTheAttribute(string attribute) {
        Assert.Equal(ApplicationFor(""), ApplicationFor(attribute));
    }

    [Fact]
    public void EveryApplicationIsEmittedForTheHttpApiV2PayloadFormat() {
        var application = ApplicationFor(
            "[LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]");

        Assert.Contains("APIGatewayHttpApiV2ProxyRequest", application);
        Assert.Contains("APIGatewayHttpApiV2ProxyResponse", application);
        Assert.DoesNotContain("global::Amazon.Lambda.APIGatewayEvents.APIGatewayProxyRequest ", application);
    }

    /// <summary>
    /// Selecting the REST integration fails the build rather than emitting a handler that cannot
    /// read what a REST API sends it.
    /// </summary>
    [Fact]
    public void SelectingTheRestApiIntegrationIsABuildError() {
        // Run rather than Generate: Generate asserts the compilation has no errors, and an
        // application with no handler emitted does not compile - which is the point.
        var result = WebGeneratorHarness.Run(
            new WebLambdaSourceGenerator(),
            WebGeneratorHarness.Application(
                attributes: "[LambdaWebApplication(Version = ProxyIntegrationType.ApiGateway)]"));

        var diagnostic = Assert.Single(
            result.GeneratorDiagnostics, d => d.Id == "HRDAWS001");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>
    /// And emits nothing. A handler alongside the error would still be a handler bound to the wrong
    /// payload format, and would go on compiling for anyone who suppressed the diagnostic.
    /// </summary>
    [Fact]
    public void SelectingTheRestApiIntegrationEmitsNoApplication() {
        // Run rather than Generate: Generate asserts the compilation has no errors, and an
        // application with no handler emitted does not compile - which is the point.
        var result = WebGeneratorHarness.Run(
            new WebLambdaSourceGenerator(),
            WebGeneratorHarness.Application(
                attributes: "[LambdaWebApplication(Version = ProxyIntegrationType.ApiGateway)]"));

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("App"));
    }

    /// <summary>
    /// The working value does not trip the diagnostic — the check reads the assignment as written,
    /// so a substring match on the wrong member name would catch both.
    /// </summary>
    [Fact]
    public void SelectingTheHttpApiV2IntegrationReportsNothing() {
        var result = WebGeneratorHarness.Generate(
            WebGeneratorHarness.Application(
                attributes: "[LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]"));

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "HRDAWS001");
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
    /// The default is the value that works, so an application that applies the attribute without
    /// saying which integration it wants gets the implemented one rather than the error.
    /// </summary>
    [Fact]
    public void TheDefaultProxyIntegrationTypeIsHttpApiV2() {
        Assert.Equal(ProxyIntegrationType.HttpApiV2, new LambdaWebApplicationAttribute().Version);
    }
}
