using Hardened.IntegrationTests.Conformance;

namespace Hardened.IntegrationTests.Smithy.SUT.Tests;

/// <summary>
/// The shared front-end conformance suite, run against the Smithy petstore.
/// </summary>
public class SmithyPetstoreConformance : PetstoreConformanceTests {
    protected override string FrontEnd => "Smithy";

    /// <summary>From PublishUrl metadata, set for the first time by this suite.</summary>
    /// <remarks>
    /// openapi.json, because that is what it now is. Publishing used to serve the contract itself,
    /// which for a Smithy model is a Smithy AST - so the path was named for the artifact rather than
    /// the format, and no OpenAPI client could read what was there. It serves the document generated
    /// from the normalised model now, the same one every other front-end publishes.
    /// </remarks>
    protected override string DocumentPath => "/openapi.json";

    /// <summary>From @httpBearerAuth on the service, which GetSecuredPet does not opt out of.</summary>
    protected override string SecuredPath => "/pets/secured";
}
