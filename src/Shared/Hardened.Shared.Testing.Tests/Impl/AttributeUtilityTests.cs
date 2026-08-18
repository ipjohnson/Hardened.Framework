using System.Reflection;
using Hardened.Shared.Testing.Impl;
using Xunit;

[assembly: Hardened.Shared.Testing.Tests.Impl.AttributeUtilityTests.Scoped("assembly")]

namespace Hardened.Shared.Testing.Tests.Impl;

/// <summary>
/// The reflection walk that feeds the attribute stack behind <c>[HardenedTest]</c>.
/// </summary>
/// <remarks>
/// <para>
/// CI measured it at <b>40% line / 38.8% branch</b>, the worst branch coverage of anything in
/// <c>Hardened.Shared.Testing</c>. <c>AttributeCollectionTests</c> covers the collection this feeds,
/// built from arrays a test hands it; nothing drove the reflection that fills those arrays from a
/// real method, class and assembly.
/// </para>
/// <para>
/// <b>The two families search in opposite directions, and that is the point of this file.</b>
/// <c>GetTestAttribute</c> returns the narrowest declaration — parameter, then method, then class,
/// then assembly, first match wins. <c>GetTestAttributes</c> accumulates the other way round —
/// assembly, class, method, parameter. Both are defensible and they are not the same rule, so a
/// caller that assumes one order and gets the other reads the wrong attribute with no error
/// anywhere.
/// </para>
/// </remarks>
public class AttributeUtilityTests {

    [AttributeUsage(
        AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method |
        AttributeTargets.Parameter,
        AllowMultiple = true)]
    public sealed class ScopedAttribute : Attribute {
        public ScopedAttribute(string scope) {
            Scope = scope;
        }

        public string Scope { get; }
    }

    private sealed class UnrelatedAttribute : Attribute { }

    private static MethodInfo MethodOf(Type type, string name) =>
        type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static ParameterInfo ParameterOf(Type type, string name) =>
        MethodOf(type, name).GetParameters()[0];

    #region fixtures

    [Scoped("class")]
    private class DecoratedClass {
        [Scoped("method")]
        public void MethodDeclares([Scoped("parameter")] string value) { }

        [Scoped("method")]
        public void MethodOnly(string value) { }

        public void ClassOnly(string value) { }
    }

    private class PlainClass {
        public void AssemblyOnly(string value) { }

        public void NoneAnywhere(string value) { }
    }

    #endregion

    #region GetTestAttribute — narrowest wins

    [Fact]
    public void AMethodPrefersItsOwnDeclaration() {
        Assert.Equal(
            "method",
            MethodOf(typeof(DecoratedClass), nameof(DecoratedClass.MethodOnly))
                .GetTestAttribute<ScopedAttribute>()!.Scope);
    }

    [Fact]
    public void AMethodFallsBackToItsClass() {
        Assert.Equal(
            "class",
            MethodOf(typeof(DecoratedClass), nameof(DecoratedClass.ClassOnly))
                .GetTestAttribute<ScopedAttribute>()!.Scope);
    }

    [Fact]
    public void AMethodFallsBackToTheAssembly() {
        Assert.Equal(
            "assembly",
            MethodOf(typeof(PlainClass), nameof(PlainClass.AssemblyOnly))
                .GetTestAttribute<ScopedAttribute>()!.Scope);
    }

    [Fact]
    public void AnAttributeDeclaredNowhereIsNull() {
        Assert.Null(
            MethodOf(typeof(PlainClass), nameof(PlainClass.NoneAnywhere))
                .GetTestAttribute<UnrelatedAttribute>());
    }

    /// <summary>
    /// The parameter is narrower than the method it is on.
    /// </summary>
    [Fact]
    public void AParameterPrefersItsOwnDeclaration() {
        Assert.Equal(
            "parameter",
            ParameterOf(typeof(DecoratedClass), nameof(DecoratedClass.MethodDeclares))
                .GetTestAttribute<ScopedAttribute>()!.Scope);
    }

    [Fact]
    public void AParameterFallsBackToItsMethod() {
        Assert.Equal(
            "method",
            ParameterOf(typeof(DecoratedClass), nameof(DecoratedClass.MethodOnly))
                .GetTestAttribute<ScopedAttribute>()!.Scope);
    }

    [Fact]
    public void AParameterFallsBackToItsClass() {
        Assert.Equal(
            "class",
            ParameterOf(typeof(DecoratedClass), nameof(DecoratedClass.ClassOnly))
                .GetTestAttribute<ScopedAttribute>()!.Scope);
    }

    [Fact]
    public void AParameterFallsBackToTheAssembly() {
        Assert.Equal(
            "assembly",
            ParameterOf(typeof(PlainClass), nameof(PlainClass.AssemblyOnly))
                .GetTestAttribute<ScopedAttribute>()!.Scope);
    }

    [Fact]
    public void AParameterAttributeDeclaredNowhereIsNull() {
        Assert.Null(
            ParameterOf(typeof(PlainClass), nameof(PlainClass.NoneAnywhere))
                .GetTestAttribute<UnrelatedAttribute>());
    }

    #endregion

    #region GetTestAttributes — every scope, widest first

    /// <summary>
    /// Every scope, not the first that matches. A method-level attribute does not suppress the
    /// class's — that is what lets a class declare a shared mock and a method add one.
    /// </summary>
    [Fact]
    public void EveryScopeContributesToAMethodsAttributes() {
        var scopes = MethodOf(typeof(DecoratedClass), nameof(DecoratedClass.MethodOnly))
            .GetTestAttributes<ScopedAttribute>()
            .Select(attribute => attribute.Scope)
            .ToArray();

        Assert.Equal(["assembly", "class", "method"], scopes);
    }

    /// <summary>
    /// <b>The opposite order to <c>GetTestAttribute</c>.</b> That method returns the narrowest
    /// declaration; this one lists the widest first. Whatever consumes the list decides which end
    /// wins, and it has to know which end it is being handed.
    /// </summary>
    [Fact]
    public void TheAccumulatedOrderIsWidestFirst() {
        var scopes = ParameterOf(typeof(DecoratedClass), nameof(DecoratedClass.MethodDeclares))
            .GetTestAttributes<ScopedAttribute>()
            .Select(attribute => attribute.Scope)
            .ToArray();

        Assert.Equal(["assembly", "class", "method", "parameter"], scopes);

        // The single-attribute lookup answers with the last of these, not the first.
        Assert.Equal(
            scopes[^1],
            ParameterOf(typeof(DecoratedClass), nameof(DecoratedClass.MethodDeclares))
                .GetTestAttribute<ScopedAttribute>()!.Scope);
    }

    [Fact]
    public void AParameterWithNoDeclarationStillCollectsTheWiderScopes() {
        var scopes = ParameterOf(typeof(DecoratedClass), nameof(DecoratedClass.ClassOnly))
            .GetTestAttributes<ScopedAttribute>()
            .Select(attribute => attribute.Scope)
            .ToArray();

        Assert.Equal(["assembly", "class"], scopes);
    }

    [Fact]
    public void AMethodOnAnUndecoratedClassStillCollectsTheAssembly() {
        Assert.Equal(
            ["assembly"],
            MethodOf(typeof(PlainClass), nameof(PlainClass.AssemblyOnly))
                .GetTestAttributes<ScopedAttribute>()
                .Select(attribute => attribute.Scope));
    }

    [Fact]
    public void AnAttributeDeclaredNowhereCollectsNothing() {
        Assert.Empty(
            MethodOf(typeof(PlainClass), nameof(PlainClass.NoneAnywhere))
                .GetTestAttributes<UnrelatedAttribute>());

        Assert.Empty(
            ParameterOf(typeof(PlainClass), nameof(PlainClass.NoneAnywhere))
                .GetTestAttributes<UnrelatedAttribute>());
    }

    /// <summary>
    /// Only the requested type. An unrelated attribute in the same scope must not be returned as
    /// one — <c>OfType</c> and the <c>is T</c> filter are what keep the two families honest.
    /// </summary>
    [Fact]
    public void AnUnrelatedAttributeInTheSameScopeIsIgnored() {
        Assert.Empty(
            MethodOf(typeof(DecoratedClass), nameof(DecoratedClass.MethodOnly))
                .GetTestAttributes<UnrelatedAttribute>());
    }

    #endregion
}
