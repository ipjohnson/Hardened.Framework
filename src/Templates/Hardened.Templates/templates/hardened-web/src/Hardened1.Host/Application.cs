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
#if (lambda)
using Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;
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
#if (lambda)
// Brings the API Gateway host and, through the [HardenedWebModule] it carries, the web pipeline.
[LambdaWebModule]
#endif
#if (codeFirst)
#if (OpenApiUi)
// Development only. The page describes every operation this service exposes and renders them
// with a script from a CDN, neither of which a deployed API obviously wants. Widen the list, or
// drop the attribute, when you have decided otherwise.
//
// The page belongs here and the document does not. This attribute names a URL to fetch, which is
// a hosting decision; [Enable<OpenApiDocumentPublishing>] makes the build write a document from
// the routes it can see, and this module declares none - so it lives on the library module beside
// them. Moving it here serves "paths": {} and nothing says so.
[HardenedOpenApiUi(Title = "Hardened1", Environments = "development")]
#endif
#endif
[TemplateModuleNameLibrary]
public partial class Application;
