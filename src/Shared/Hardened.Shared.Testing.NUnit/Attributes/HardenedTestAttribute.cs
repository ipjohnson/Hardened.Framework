using DependencyModules.NUnit.Attributes;

namespace Hardened.Shared.Testing.Attributes;

/// <summary>
/// Marks a test the DependencyModules runner drives under NUnit: the container is built from the
/// attributes in scope, the parameters resolved from it, and the container disposed in the
/// runner's own <c>finally</c> when the test has run.
/// </summary>
/// <remarks>
/// The same name and namespace as the xUnit attribute in <c>Hardened.Shared.Testing.xUnit</c>, on
/// purpose: a test project references one runner package, and its tests read the same either way.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class HardenedTestAttribute : ModuleTestAttribute {

    // NUnit instantiates the attribute while it discovers tests, so this runs before any test
    // does, and the seam the harness reads is in place by the time anything reads it.
    static HardenedTestAttribute() => NUnitCurrentTestProvider.Install();

    public HardenedTestAttribute() {
    }
}
