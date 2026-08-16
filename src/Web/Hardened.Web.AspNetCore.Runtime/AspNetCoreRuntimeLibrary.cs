using DependencyModules.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.Web.AspNetCore.Runtime;

/// <summary>
/// Hosting for Hardened inside the ASP.NET Core request pipeline, applied to an application as
/// <c>[AspNetCoreRuntime]</c> and inserted with <c>app.UseHardened()</c>.
///
/// <para>
/// <c>[HardenedWebModule]</c> is what brings the web pipeline — <c>IWebExecutionHandlerService</c>,
/// the routing table and the request pipeline underneath it. Without it this module registered the
/// ASP.NET host and nothing to serve with, and the constructor the web generator emits resolves
/// <c>IWebExecutionHandlerService</c> unconditionally, so an application that imported only this
/// module threw on its first request:
/// </para>
/// <code>
/// No service for type 'Hardened.Web.Runtime.Handlers.IWebExecutionHandlerService' has been registered.
/// </code>
/// <para>
/// That is exactly the shape the README's Quick Start documents, so the snippet everyone copies
/// first did not start. Both in-repo web samples hid it — one declares <c>[HardenedWebModule]</c>
/// explicitly, the other inherits it from a library referenced for unrelated reasons — which is why
/// a green integration suite never caught it. <c>LambdaWebModule</c> had the identical omission,
/// fixed 2026-08-15; this is the same pairing for the ASP.NET host.
/// </para>
/// <para>
/// Importing it twice is harmless: modules deduplicate by equality, so an application that also
/// declares <c>[HardenedWebModule]</c> itself is unaffected.
/// </para>
/// </summary>
[DependencyModule]
[HardenedWebModule]
public partial class AspNetCoreRuntime { }
