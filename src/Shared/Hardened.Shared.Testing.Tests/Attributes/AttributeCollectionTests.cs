using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Tests.Infrastructure;
using Xunit;

namespace Hardened.Shared.Testing.Tests.Attributes;

/// <summary>
/// <see cref="AttributeCollection"/> is where "the narrower declaration wins" is actually decided.
/// Every attribute the harness honours — the entry point, the environment name, the registration and
/// startup hooks — is found through it, so the rules below are the ones a consumer sees when they
/// move an attribute from a class up to an assembly.
/// </summary>
public class AttributeCollectionTests {

    private sealed class Marker : Attribute {
        public Marker(string scope) {
            Scope = scope;
        }

        public string Scope { get; }
    }

    private sealed class Unrelated : Attribute { }

    private sealed class OrderedMarker : Attribute, IHardenedOrderedAttribute {
        public OrderedMarker(string scope, int order) {
            Scope = scope;
            Order = order;
        }

        public string Scope { get; }

        public int Order { get; }
    }

    private static AttributeCollection Collection(
        object[]? method = null, object[]? @class = null, object[]? assembly = null) =>
        new(method ?? [], @class ?? [], assembly ?? []);

    [Fact]
    public void MethodLevelDeclarationBeatsClassAndAssembly() {
        var collection = Collection(
            method: [new Marker("method")],
            @class: [new Marker("class")],
            assembly: [new Marker("assembly")]);

        Assert.Equal("method", collection.GetAttribute<Marker>()?.Scope);
    }

    [Fact]
    public void ClassLevelDeclarationBeatsAssemblyWhenTheMethodDeclaresNone() {
        var collection = Collection(
            @class: [new Marker("class")],
            assembly: [new Marker("assembly")]);

        Assert.Equal("class", collection.GetAttribute<Marker>()?.Scope);
    }

    [Fact]
    public void AssemblyLevelDeclarationIsUsedWhenNothingNarrowerDeclaresOne() {
        var collection = Collection(assembly: [new Marker("assembly")]);

        Assert.Equal("assembly", collection.GetAttribute<Marker>()?.Scope);
    }

    [Fact]
    public void MissingAttributeIsNullRatherThanAnError() {
        var collection = Collection(method: [new Unrelated()]);

        Assert.Null(collection.GetAttribute<Marker>());
    }

    [Fact]
    public void EnumerationRunsNarrowestScopeFirst() {
        var collection = Collection(
            method: [new Marker("method")],
            @class: [new Marker("class")],
            assembly: [new Marker("assembly")]);

        Assert.Equal(
            new[] { "method", "class", "assembly" },
            collection.OfType<Marker>().Select(marker => marker.Scope));
    }

    [Fact]
    public void GetAttributesCollectsEveryScopeRatherThanStoppingAtTheFirst() {
        var collection = Collection(
            method: [new Marker("method")],
            @class: [new Marker("class")],
            assembly: [new Marker("assembly"), new Unrelated()]);

        Assert.Equal(3, collection.GetAttributes<Marker>().Count);
    }

    /// <summary>
    /// The rule that makes <see cref="IHardenedOrderedAttribute"/> worth having: a hook declared on
    /// an assembly runs before one declared on the method when it asks to, so a package can put
    /// itself in front of the tests that consume it.
    /// </summary>
    [Fact]
    public void DeclaredOrderOutranksScopeForOrderedAttributes() {
        var collection = Collection(
            method: [new OrderedMarker("method", 30)],
            @class: [new OrderedMarker("class", 10)],
            assembly: [new OrderedMarker("assembly", 20)]);

        Assert.Equal(
            new[] { "class", "assembly", "method" },
            collection.GetAttributes<OrderedMarker>().Select(marker => marker.Scope));
    }

    [Fact]
    public void UnorderedAttributesKeepScopeOrder() {
        var collection = Collection(
            method: [new Marker("method")],
            @class: [new Marker("class")],
            assembly: [new Marker("assembly")]);

        Assert.Equal(
            new[] { "method", "class", "assembly" },
            collection.GetAttributes<Marker>().Select(marker => marker.Scope));
    }

    /// <summary>
    /// Sorting is requested from the type argument, not from the instances. Asking for a plain
    /// <c>Attribute</c> that happens to be ordered leaves the list in scope order.
    /// </summary>
    [Fact]
    public void OrderingIsDecidedByTheRequestedTypeNotTheInstances() {
        var collection = Collection(
            method: [new OrderedMarker("method", 30)],
            @class: [new OrderedMarker("class", 10)]);

        Assert.Equal(
            new[] { "method", "class" },
            collection.GetAttributes<Attribute>().OfType<OrderedMarker>().Select(marker => marker.Scope));
    }

    [RecordingRegistration("class")]
    private class DeclaresAttributesAtEveryLevel {
        [RecordingRegistration("method")]
        public void Method() { }
    }

    /// <summary>
    /// The reflection half. Everything above works on a collection built by hand; this is what
    /// proves the three buckets are filled from the three places a consumer can declare an
    /// attribute, including the assembly — see Bootstrap.cs.
    /// </summary>
    [Fact]
    public void FromMethodInfoFillsAllThreeScopesFromReflection() {
        var method = typeof(DeclaresAttributesAtEveryLevel).GetMethod(nameof(DeclaresAttributesAtEveryLevel.Method))!;

        var collection = AttributeCollection.FromMethodInfo(method);

        Assert.Equal("method",
            Assert.IsType<RecordingRegistrationAttribute>(Assert.Single(collection.MethodAttributes)).Name);
        Assert.Equal("class",
            Assert.IsType<RecordingRegistrationAttribute>(Assert.Single(collection.ClassAttributes)).Name);
        Assert.Contains(collection.AssemblyAttributes, attribute => attribute is EnvironmentNameAttribute);
    }
}
