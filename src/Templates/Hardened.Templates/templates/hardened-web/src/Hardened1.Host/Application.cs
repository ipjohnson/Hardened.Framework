using Hardened.Shared.Runtime.Attributes;
#if (codeFirst)
using Hardened.Web.Runtime.OpenApi;
#endif
#if (kestrel)
using Hardened.Web.Kestrel.Runtime;
#endif
#if (aspnet)
using Hardened.Web.AspNetCore.Runtime;
#endif

namespace Hardened1.Host;

/// <summary>
/// The application module: which runtime this runs on, and which libraries come along.
/// </summary>
/// <remarks>
/// Each attribute is the generated companion of a module class, so composing a runtime and
/// composing your own library are the same mechanism.
/// </remarks>
[HardenedModule]
#if (kestrel)
[KestrelRuntime]
#endif
#if (aspnet)
[AspNetCoreRuntime]
#endif
#if (codeFirst)
// Embeds the document the build wrote from this application's routes, and serves it at
// /openapi.json. Spec-first applications do not use this: their document is a build input,
// published by PublishUrl on the spec item instead.
[Enable<OpenApiDocumentPublishing>]
#if (OpenApiUi)
// Development only. The page describes every operation this service exposes and renders them
// with a script from a CDN, neither of which a deployed API obviously wants. Widen the list, or
// drop the attribute, when you have decided otherwise.
[HardenedOpenApiUi(Title = "Hardened1", Environments = "development")]
#endif
#endif
[Hardened1Library]
public partial class Application { }
