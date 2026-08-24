using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;

namespace Hardened.IntegrationTests.Conformance.CodeFirst.SUT;

/// <summary>
/// The entry point the generated routing table is anchored on.
/// </summary>
/// <remarks>
/// The code-first arm of the front-end conformance suite. The OpenAPI and Smithy fixtures reach the
/// same three operations through a description and Hardened.Idl.SourceGenerator; this one declares
/// them in C# and reaches them through Hardened.Web.SourceGenerator. Both now share one routing
/// table generator, and this fixture is part of what keeps that from silently ceasing to be true.
/// </remarks>
[HardenedModule]
[HardenedWebModule]
// Code-first asks for a published description with a feature marker carrying the path.
// The described front-ends ask with <PublishUrl> metadata on the spec item. Three vocabularies,
// one behaviour - which is the row of the conformance suite this enables.
[Enable<OpenApiDocumentPublishing>]
public partial class CodeFirstTestApp {
}
