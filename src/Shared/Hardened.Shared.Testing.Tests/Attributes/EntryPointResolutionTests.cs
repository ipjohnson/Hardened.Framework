using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Impl;
using Hardened.Shared.Testing.Tests.Infrastructure;
using Xunit;

namespace Hardened.Shared.Testing.Tests.Attributes;

/// <summary>
/// <c>[HardenedTestEntryPoint]</c> can be declared on an assembly, a class or a method, and every
/// consumer of it — <c>Hardened.Web.Testing</c>'s <c>WebTestingAttribute</c> among them — finds it
/// through <see cref="AttributeUtility.GetTestAttribute{T}(System.Reflection.MethodInfo)"/>. These
/// pin which declaration that lookup answers with.
/// </summary>
/// <remarks>
/// The assembly rung comes from <c>[assembly: HardenedTestEntryPoint(typeof(AssemblyEntryPointModule))]</c>
/// in Bootstrap.cs. It is the one level a test cannot declare locally, which is exactly why it is
/// declared once for the whole project rather than mocked.
/// </remarks>
public class EntryPointResolutionTests {

    private class InheritsTheAssemblyEntryPoint {
        public void Method() { }
    }

    [HardenedTestEntryPoint(typeof(ClassEntryPointModule))]
    private class DeclaresAClassEntryPoint {
        public void Method() { }

        [HardenedTestEntryPoint(typeof(MethodEntryPointModule))]
        public void MethodWithItsOwnEntryPoint() { }
    }

    [Fact]
    public void AMethodWithNoNearerDeclarationGetsTheAssemblyEntryPoint() {
        var method = typeof(InheritsTheAssemblyEntryPoint)
            .GetMethod(nameof(InheritsTheAssemblyEntryPoint.Method))!;

        Assert.Equal(typeof(AssemblyEntryPointModule),
            method.GetTestAttribute<HardenedTestEntryPointAttribute>()?.EntryPoint);
    }

    [Fact]
    public void AClassEntryPointBeatsTheAssemblyEntryPoint() {
        var method = typeof(DeclaresAClassEntryPoint).GetMethod(nameof(DeclaresAClassEntryPoint.Method))!;

        Assert.Equal(typeof(ClassEntryPointModule),
            method.GetTestAttribute<HardenedTestEntryPointAttribute>()?.EntryPoint);
    }

    [Fact]
    public void AMethodEntryPointBeatsBothTheClassAndTheAssembly() {
        var method = typeof(DeclaresAClassEntryPoint)
            .GetMethod(nameof(DeclaresAClassEntryPoint.MethodWithItsOwnEntryPoint))!;

        Assert.Equal(typeof(MethodEntryPointModule),
            method.GetTestAttribute<HardenedTestEntryPointAttribute>()?.EntryPoint);
    }

    /// <summary>
    /// Lookup picks one. Module loading does not — <c>ModuleTestCase</c> asks every
    /// <c>IDependencyModuleProvider</c> in scope for its module and loads all of them, so a method
    /// entry point adds to the assembly's registrations rather than replacing them. The two rules
    /// read alike and are not, so both are pinned.
    /// </summary>
    [Fact]
    public void EveryDeclaredEntryPointIsStillVisibleToModuleLoading() {
        var method = typeof(DeclaresAClassEntryPoint)
            .GetMethod(nameof(DeclaresAClassEntryPoint.MethodWithItsOwnEntryPoint))!;

        var entryPoints = method.GetTestAttributes<HardenedTestEntryPointAttribute>()
            .Select(attribute => attribute.EntryPoint)
            .ToArray();

        Assert.Contains(typeof(AssemblyEntryPointModule), entryPoints);
        Assert.Contains(typeof(ClassEntryPointModule), entryPoints);
        Assert.Contains(typeof(MethodEntryPointModule), entryPoints);
    }

    /// <summary>
    /// GetTestAttributes walks widest scope first, the reverse of the single-attribute lookup. That
    /// is what makes an assembly-level hook run before a method-level one of the same order.
    /// </summary>
    [Fact]
    public void GetTestAttributesWalksWidestScopeFirst() {
        var method = typeof(DeclaresAClassEntryPoint)
            .GetMethod(nameof(DeclaresAClassEntryPoint.MethodWithItsOwnEntryPoint))!;

        var entryPoints = method.GetTestAttributes<HardenedTestEntryPointAttribute>()
            .Select(attribute => attribute.EntryPoint)
            .ToArray();

        Assert.Equal(
            new[] { typeof(AssemblyEntryPointModule), typeof(ClassEntryPointModule), typeof(MethodEntryPointModule) },
            entryPoints);
    }

    [Fact]
    public void GetModuleBuildsAnInstanceOfTheDeclaredEntryPoint() {
        var attribute = new HardenedTestEntryPointAttribute(typeof(AssemblyEntryPointModule));

        Assert.IsType<AssemblyEntryPointModule>(attribute.GetModule());
    }

    [Fact]
    public void GetModuleBuildsANewInstanceEachTimeItIsAsked() {
        var attribute = new HardenedTestEntryPointAttribute(typeof(AssemblyEntryPointModule));

        Assert.NotSame(attribute.GetModule(), attribute.GetModule());
    }
}
