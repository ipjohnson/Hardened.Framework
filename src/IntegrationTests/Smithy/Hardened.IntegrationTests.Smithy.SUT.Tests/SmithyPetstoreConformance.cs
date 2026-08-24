using Hardened.IntegrationTests.Conformance;

namespace Hardened.IntegrationTests.Smithy.SUT.Tests;

/// <summary>
/// The shared front-end conformance suite, run against the Smithy petstore.
/// </summary>
public class SmithyPetstoreConformance : PetstoreConformanceTests {
    protected override string FrontEnd => "Smithy";

    /// <summary>From PublishUrl metadata, set for the first time by this suite.</summary>
    protected override string DocumentPath => "/smithy.json";

    /// <summary>From @httpBearerAuth on the service, which GetSecuredPet does not opt out of.</summary>
    protected override string SecuredPath => "/pets/secured";
}
