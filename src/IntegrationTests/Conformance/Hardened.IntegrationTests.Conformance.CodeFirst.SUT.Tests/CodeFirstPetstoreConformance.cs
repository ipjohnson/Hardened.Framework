namespace Hardened.IntegrationTests.Conformance.CodeFirst.SUT.Tests;

/// <summary>
/// The shared front-end conformance suite, run against the attribute-routed petstore.
/// </summary>
public class CodeFirstPetstoreConformance : PetstoreConformanceTests {
    protected override string FrontEnd => "code-first";

    /// <summary>From [OpenApiDocumentPath] on the enabled feature marker.</summary>
    protected override string DocumentPath => "/openapi.json";

    /// <summary>From [AuthorizeGrants("pets:read")] on the handler.</summary>
    protected override string SecuredPath => "/pets/secured";

    /// <summary>
    /// 404, not the 400 the described front-ends answer. See MalformedTokenStatus - a route
    /// constraint is compiled into the table here, so violating it means the route did not match.
    /// Delete this override when the divergence is resolved.
    /// </summary>
    protected override int MalformedTokenStatus => 404;
}
