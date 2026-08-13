using DependencyModules.Runtime.Attributes;

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
/// </summary>
[DependencyModule]
public partial class KestrelRuntime { }
