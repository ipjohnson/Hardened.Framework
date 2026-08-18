
namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// A specification-first application serves the specification it was built from, byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// The contract is a build input here, so re-emitting it could only produce a second description
/// of the same thing - one that agreed with the first as far as the emitter had been taught to and
/// silently diverged everywhere else. Serving the source text makes fidelity a property of the
/// arrangement rather than something to keep testing: descriptions, examples, security schemes,
/// vendor extensions, ordering and comments all survive, including everything Hardened's own model
/// does not represent.
/// </para>
/// <para>
/// The byte comparison is the assertion that matters. Anything weaker - "it parses", "it has the
/// same paths" - would pass for a re-emitted document too, which is the failure this design exists
/// to make impossible rather than to detect.
/// </para>
/// </remarks>
public class ServedSpecificationTests {

    [HardenedTest]
    public async Task TheServedDocumentIsTheSourceSpecification(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/openapi.yaml");

        response.Assert.Ok();

        Assert.Equal(PetstoreSpecification.Document, await response.ReadTextAsync());
    }

    /// <summary>
    /// And it carries the type it is actually written in. A YAML document served as
    /// <c>application/json</c> is one a client cannot read.
    /// </summary>
    [HardenedTest]
    public async Task TheContentTypeMatchesTheSourceFormat(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/openapi.yaml");

        Assert.Equal("application/yaml", response.Headers["Content-Type"].ToString());
    }

    /// <summary>
    /// The served document is the file, not a normalisation of it.
    /// </summary>
    /// <remarks>
    /// No OpenAPI model carries comments, so a comment reaching the wire cannot have come from
    /// anything that re-serialised the document. It is the one assertion here that a
    /// re-emitted-but-faithful document could not also pass.
    /// </remarks>
    [HardenedTest]
    public async Task AYamlCommentReachesTheWire(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/openapi.yaml");

        Assert.Contains("# This comment is load-bearing.", await response.ReadTextAsync());
    }
}
