using DependencyModules.Runtime.Attributes;
using Hardened.IntegrationTests.Benchmark.SUT.Models;
using Hardened.Templates.RazorBlade;

namespace Hardened.IntegrationTests.Benchmark.SUT;

/// <summary>
/// Maps the name in the spec's <c>x-hardened-template</c> onto the compiled view.
/// </summary>
/// <remarks>
/// This lives in the application because RazorBlade generates template classes as
/// <c>internal</c> - <c>Views.Fortunes</c> is not nameable from the Hardened package that consumes
/// it. The lambda is where the untyped model the engine holds becomes the typed one the template
/// needs, with the cast written by the compiler rather than by reflection.
/// </remarks>
[SingletonService]
public class BenchmarkTemplates : IRazorBladeTemplateSource {
    public IEnumerable<RazorBladeTemplateDescriptor> Templates => [
        RazorBladeTemplate.Html<FortunePage>("Fortunes", model => new Views.Fortunes(model))
    ];
}
