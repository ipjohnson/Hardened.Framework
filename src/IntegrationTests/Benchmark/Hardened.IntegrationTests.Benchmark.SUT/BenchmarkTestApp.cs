using Hardened.Shared.Runtime.Attributes;
using Hardened.Templates.RazorBlade;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Benchmark.SUT;

/// <summary>
/// The TechEmpower routes as one Hardened application.
/// </summary>
/// <remarks>
/// <c>RazorBladeTemplateLibrary</c> is listed last deliberately. The serializer locator tests later
/// registrations first, so this is what puts a template response ahead of JSON for <c>/fortunes</c>,
/// whose request would otherwise satisfy both.
/// </remarks>
[HardenedModule]
[HardenedWebModule]
[RazorBladeTemplateLibrary]
public partial class BenchmarkTestApp;
