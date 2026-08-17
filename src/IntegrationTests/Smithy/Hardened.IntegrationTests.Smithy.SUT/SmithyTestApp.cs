using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Smithy.SUT;

/// <summary>
/// The entry point the generated routing table is anchored on.
/// </summary>
/// <remarks>
/// Nothing here mentions Smithy. The routing table, the handler registrations and the JSON type
/// info resolver are all generated against this type by <c>Hardened.Idl.SourceGenerator</c>, from
/// the model the Smithy build task wrote - the same generator, and the same code path, that the
/// OpenAPI fixture next door exercises.
/// </remarks>
[HardenedModule]
[HardenedWebModule]
public partial class SmithyTestApp {
}
