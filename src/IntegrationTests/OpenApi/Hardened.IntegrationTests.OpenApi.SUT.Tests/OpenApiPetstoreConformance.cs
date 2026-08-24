using Hardened.IntegrationTests.Conformance;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// The shared front-end conformance suite, run against the OpenAPI petstore.
/// </summary>
public class OpenApiPetstoreConformance : PetstoreConformanceTests {
    protected override string FrontEnd => "OpenAPI";

    /// <summary>
    /// This fixture fabricates a pet for any id but this one, and DeclaredStatusTests depends on
    /// <c>/pets/7</c> answering 200.
    /// </summary>
    protected override string AbsentPetId => "missing";

    /// <summary>From PublishUrl metadata on the spec item.</summary>
    protected override string DocumentPath => "/openapi.yaml";
}
