using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// A specification-first application, which registers nothing of its own.
/// </summary>
/// <remarks>
/// <para>
/// It used to register an <c>OpenApiDocumentProvider</c> by hand, naming the generated
/// <c>PetstoreSpecification</c>, the path and the content type. All three are now facts about the
/// spec file, so all three live on the item that declares it in the csproj and the registration is
/// generated - including the reference page, which goes through the same
/// <c>HardenedOpenApiUi</c> module an attribute-routed application applies as an attribute.
/// </para>
/// <para>
/// The content type travels with the document rather than being restated: a YAML specification is
/// served as YAML, because converting it to fit a conventional <c>/openapi.json</c> would put an
/// emitter back in the path that exists to have none.
/// </para>
/// </remarks>
[HardenedModule]
[HardenedWebModule]
public partial class OpenApiTestApp { }
