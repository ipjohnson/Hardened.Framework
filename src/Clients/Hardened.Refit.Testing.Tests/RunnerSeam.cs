using System.Runtime.CompilerServices;
using Hardened.Shared.Testing;

namespace Hardened.Refit.Testing.Tests;

/// <summary>
/// Installs the xUnit running-test seam for this assembly's tests. A test project that declares a
/// <c>[HardenedTest]</c> gets it from that attribute's assembly; this one drives the harness
/// directly, so nothing else would load the runner package.
/// </summary>
internal static class RunnerSeam {

    [ModuleInitializer]
    internal static void Install() => XunitCurrentTestProvider.Install();
}
