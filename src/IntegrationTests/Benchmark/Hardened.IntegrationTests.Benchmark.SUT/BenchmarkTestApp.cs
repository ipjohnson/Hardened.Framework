using Hardened.Shared.Runtime.Attributes;
using Hardened.Templates.RazorBlade;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Benchmark.SUT;

/// <summary>
/// The TechEmpower routes as one Hardened application.
/// </summary>
/// <remarks>
/// <c>[Enable&lt;HardenedRazorTemplate&gt;]</c> generates
/// <c>BenchmarkTestAppRazorTemplate&lt;TModel&gt;</c>, which Views/Fortunes.cshtml inherits.
/// Naming the marker is what references the package, so there is nothing to detect and nothing to
/// register - the view renders itself.
/// </remarks>
[HardenedModule]
[HardenedWebModule]
[Enable<HardenedRazorTemplate>]
public partial class BenchmarkTestApp;
