using Hardened.IntegrationTests.Conformance;

namespace Hardened.IntegrationTests.Smithy.SUT.Tests;

/// <summary>
/// The shared front-end conformance suite, run against the Smithy petstore.
/// </summary>
public class SmithyPetstoreConformance : PetstoreConformanceTests {
    protected override string FrontEnd => "Smithy";
}
