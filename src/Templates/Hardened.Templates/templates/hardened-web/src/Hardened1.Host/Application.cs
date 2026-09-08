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
using Hardened.Aws.Lambda.ApiGateway;
#endif
#if (cloudRun)
using Hardened.Gcp.CloudRun.Runtime;
#endif
#if (azureFunctions)
using Hardened.Azure.Functions.Http;
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
// The API Gateway payload adapter, and through the runtime module it composes, the invocation loop
// and the web pipeline. Named here rather than inferred: a function whose handlers sit beside its
// entry point gets its adapter from their trigger attributes, but the handlers here are in the
// library project, and a generator only sees the compilation it runs in. The Kestrel and ASP.NET
// hosts name theirs above for the same reason.
[ApiGatewayModule]
#endif
#if (cloudRun)
// The host. A Cloud Run service is a container listening on a port, so this is named the way
// [KestrelRuntime] is, and it composes that runtime: Kestrel on PORT, a SIGTERM drain, and a front
// door ahead of routing that serves the trigger attributes beside these routes when an adapter
// package is referenced.
[CloudRunRuntime]
#endif
#if (azureFunctions)
// The HTTP trigger adapter, and through the runtime module it composes, the worker registration
// and the web pipeline. Named here rather than inferred, the way a Lambda host names its API
// Gateway module: a function app whose routes sit beside its entry point gets its HTTP function
// from the verbs, but the routes here are in the library project, and a generator only sees the
// compilation it runs in. Writing it is also what makes the generator write the one HTTP
// function the Functions host indexes.
[HttpModule]
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
