using DependencyModules.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.Web.Kestrel.Runtime;

/// <summary>
/// Hosting for Hardened directly on Kestrel, without the ASP.NET Core request pipeline.
///
/// <c>IServer.StartAsync</c> takes an <c>IHttpApplication&lt;TContext&gt;</c>, not a
/// <c>RequestDelegate</c>. <c>HostingApplication</c> — the piece that builds an
/// <c>HttpContext</c>, opens the DI scope and raises the hosting diagnostics — is merely the
/// default implementation of that interface, and nothing about Kestrel requires it. This module
/// supplies Hardened's own implementation instead:
///
///   Kestrel -> HostingApplication -> HttpContext -> ASP.NET middleware
///           -> HardenedMiddleware -> AspNetExecutionContext -> chain   (Hardened.Web.AspNetCore)
///
///   Kestrel -> HardenedHttpApplication -> chain                        (this)
///
/// Kestrel itself is unchanged — HTTP/1.1, HTTP/2, TLS, connection lifecycle and header parsing
/// all still come from it. What is skipped is the per-request cost of the layers above it,
/// measured at 11-25% of total request time depending on how much a route binds.
///
/// This is additive. <c>Hardened.Web.AspNetCore.Runtime</c> remains the right choice for
/// applications that want the ASP.NET middleware ecosystem — authentication, rate limiting,
/// forwarded headers, and the standard OpenTelemetry instrumentation, none of which exist here.
/// See the readme for the full list of what is given up.
///
/// <para>
/// <c>[HardenedWebModule]</c> brings the web pipeline this host serves with —
/// <c>IWebExecutionHandlerService</c>, the routing table and the request pipeline underneath it.
/// It was absent here for the same reason it was absent on <c>AspNetCoreRuntime</c> and
/// <c>LambdaWebModule</c>: every sample declared it separately, so nothing ever exercised the
/// module on its own. Importing it twice is harmless — modules deduplicate by equality.
/// </para>
/// </summary>
[DependencyModule]
[HardenedWebModule]
public partial class KestrelRuntime { }
