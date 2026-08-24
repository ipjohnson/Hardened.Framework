namespace Hardened.IntegrationTests.Conformance.CodeFirst.SUT.Tests;

/// <summary>
/// The shared front-end conformance suite, run against the attribute-routed petstore.
/// </summary>
public class CodeFirstPetstoreConformance : PetstoreConformanceTests {
    protected override string FrontEnd => "code-first";
}
