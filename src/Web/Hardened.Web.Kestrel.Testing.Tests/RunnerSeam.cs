using System.Runtime.CompilerServices;
using Hardened.Shared.Testing;

namespace Hardened.Web.Kestrel.Testing.Tests;

/// <summary>
/// Installs the xUnit running-test seam for this assembly's tests, which drive the host directly
/// and declare no <c>[HardenedTest]</c>; <c>LastResponse</c> is keyed on it.
/// </summary>
internal static class RunnerSeam {

    [ModuleInitializer]
    internal static void Install() => XunitCurrentTestProvider.Install();
}
