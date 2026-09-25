using System.Runtime.CompilerServices;
using DependencyModules.xUnit.Attributes;

namespace Hardened.Web.AspNetCore.Testing.Tests;

/// <summary>
/// Installs the xUnit running-test seam for this assembly's tests, which drive the host directly
/// and declare no <c>[ModuleTest]</c>; <c>LastResponse</c> is keyed on it. DependencyModules.xUnit
/// installs its provider from the static constructor of <see cref="ModuleTestAttribute"/>.
/// </summary>
internal static class RunnerSeam
{
    [ModuleInitializer]
    internal static void Install() =>
        RuntimeHelpers.RunClassConstructor(typeof(ModuleTestAttribute).TypeHandle);
}
