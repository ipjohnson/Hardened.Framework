using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.Benchmarks.Sut;

/// <summary>
/// The Hardened application under benchmark.
///
/// <c>AspNetCoreRuntime</c> is present so a single SUT can serve both measured deployments —
/// the transport-free one and the one behind ASP.NET's adapter. It only adds service
/// registrations; which filter chain actually gets built is decided by the harness, which
/// constructs a separate provider per deployment. That separation matters: both
/// <c>AspNetCoreExtensions.UseHardened</c> and the transport-free bootstrap call
/// <c>IMiddlewareService.Use</c>, and <c>MiddlewareService</c> is a singleton holding a plain
/// list, so wiring both against one provider would register the web filter twice and run it
/// twice per request.
///
/// <c>ISumService</c> is not registered here. It lives in the contracts assembly without DI
/// attributes and is added by the harness with <c>AddTransient</c>, identically for Hardened and
/// for ASP.NET, so that service resolution cost is common to both rather than an artifact of
/// which container populated it.
/// </summary>
[HardenedModule]
[HardenedWebModule]
[AspNetCoreRuntime]
[KestrelRuntime]
public partial class BenchmarkApplication { }
