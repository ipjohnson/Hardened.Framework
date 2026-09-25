using System.Runtime.CompilerServices;
using DependencyModules.xUnit.Attributes;

namespace Hardened.Web.Testing.Tests;

/// <summary>
/// Installs the xUnit running-test seam for this assembly's tests. DependencyModules.xUnit
/// installs its provider from the static constructor of <see cref="ModuleTestAttribute"/>, which
/// xUnit runs when it reads a <c>[ModuleTest]</c>. This assembly drives the harness directly and
/// declares none, so it runs that constructor itself.
/// </summary>
internal static class RunnerSeam
{
    [ModuleInitializer]
    internal static void Install() =>
        RuntimeHelpers.RunClassConstructor(typeof(ModuleTestAttribute).TypeHandle);
}
