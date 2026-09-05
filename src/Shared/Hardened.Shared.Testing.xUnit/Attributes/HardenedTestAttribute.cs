using DependencyModules.xUnit.Impl;
using Xunit;
using Xunit.v3;

namespace Hardened.Shared.Testing.Attributes;

/// <summary>
/// Marks a test the DependencyModules runner drives: the container is built from the attributes
/// in scope, the parameters resolved from it, and the container disposed when the case has run.
/// </summary>
[XunitTestCaseDiscoverer(typeof(ModuleTestDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class HardenedTestAttribute : FactAttribute {

    // xUnit instantiates the attribute while it discovers tests, so this runs before any test
    // does, and the seam the harness reads is in place by the time anything reads it.
    static HardenedTestAttribute() => XunitCurrentTestProvider.Install();
}
