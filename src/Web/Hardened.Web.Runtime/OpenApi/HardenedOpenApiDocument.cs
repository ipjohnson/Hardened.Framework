namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// The feature marker that embeds this application's generated OpenAPI document and serves it at
/// <c>/openapi.json</c>.
///
/// <code>
/// [HardenedModule]
/// [HardenedWebModule]
/// [Enable&lt;HardenedOpenApiDocument&gt;]
/// public partial class Application { }
/// </code>
///
/// <para>
/// <b>Why a marker rather than a module property.</b> The document is a field on the entry point's
/// own generated partial, and the registration has to name it - so the registration can only be
/// emitted where the field is, by the generator that wrote it. An attribute argument cannot carry
/// the document either: it is <c>static readonly</c> rather than <c>const</c>, deliberately, because
/// a constant that size is inlined into every assembly that references it.
/// </para>
///
/// <para>
/// <b>It gates the embedding, not only the route.</b> Without it nothing is emitted, so an
/// application that does not serve a document does not carry one. That is a change from when the
/// document was written unconditionally: the cost was one string then, and a string is what
/// <c>scripts/extract-openapi.py</c> reads for the Spectral step, so a build that lints its
/// contract has to enable this.
/// </para>
///
/// <para>
/// Spec-first applications do not use this. Their document is a build input, embedded verbatim by
/// <c>SpecificationDocumentEmitter</c> as <c>&lt;FileName&gt;Specification.Document</c>, and it is
/// registered by hand because the content type travels with it.
/// </para>
/// </summary>
[OpenApiDocumentPath("/openapi.json")]
public sealed class HardenedOpenApiDocument { }
