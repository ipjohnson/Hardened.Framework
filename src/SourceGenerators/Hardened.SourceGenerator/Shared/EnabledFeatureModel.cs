using CSharpAuthor;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// One optional feature an entry point turned on with <c>[Enable&lt;T&gt;]</c>, together with what
/// its marker declares about itself.
/// </summary>
/// <remarks>
/// <para>
/// The facets are the whole point. A generator that switched on the marker's <em>name</em> would
/// need a change for every new feature, and the extensibility would be fictional - so the marker
/// carries what the generator needs as attributes on itself, and the generator reads them without
/// knowing which marker it is looking at. A Fluid or Mustache package ships a marker with its own
/// <c>[TemplateBase]</c> and <c>[TemplateContentType]</c> and needs no generator change at all.
/// </para>
/// <para>
/// Resolved during the syntax transform, which is the only place the marker's symbol exists. What
/// survives is this: names, strings and type definitions, all comparable by value so the model
/// still keys an incremental cache.
/// </para>
/// </remarks>
public class EnabledFeatureModel : IEquatable<EnabledFeatureModel> {
    public EnabledFeatureModel(
        ITypeDefinition markerType, IReadOnlyList<FeatureFacet> facets, bool isDependencyModule) {
        MarkerType = markerType;
        Facets = facets;
        IsDependencyModule = isDependencyModule;
    }

    /// <summary>The type named in <c>[Enable&lt;T&gt;]</c>.</summary>
    public ITypeDefinition MarkerType { get; }

    /// <summary>
    /// Whether the marker is also a DependencyModules module, so enabling it should bring its
    /// registrations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A feature that ships services and a generated type is then one attribute rather than two.
    /// <c>[Enable&lt;T&gt;]</c> constrains <c>T</c> to <c>new()</c>, which is what a module needs
    /// anyway, so this costs nothing to allow.
    /// </para>
    /// <para>
    /// Detected two ways because there are two situations. A marker from a referenced package is
    /// already compiled, so it implements <c>IDependencyModule</c> in metadata and the semantic
    /// model can see it. A marker declared in this compilation gets that interface from
    /// DependencyModules' own generator, which this one cannot see - but its
    /// <c>[DependencyModule]</c> attribute is in source, and that is enough.
    /// </para>
    /// </remarks>
    public bool IsDependencyModule { get; }

    /// <summary>What the marker declares about itself, one entry per attribute on it.</summary>
    public IReadOnlyList<FeatureFacet> Facets { get; }

    /// <summary>The facet with this name, or null. Names carry no <c>Attribute</c> suffix.</summary>
    public FeatureFacet? Facet(string name) {
        foreach (var facet in Facets) {
            if (string.Equals(facet.Name, name, StringComparison.Ordinal)) {
                return facet;
            }
        }

        return null;
    }

    public bool Equals(EnabledFeatureModel? other) =>
        other != null &&
        MarkerType.Equals(other.MarkerType) &&
        IsDependencyModule == other.IsDependencyModule &&
        Facets.Count == other.Facets.Count &&
        !Facets.Where((facet, index) => !facet.Equals(other.Facets[index])).Any();

    public override bool Equals(object obj) => Equals(obj as EnabledFeatureModel);

    public override int GetHashCode() {
        unchecked {
            var hashCode = MarkerType.GetHashCode();

            hashCode = (hashCode * 397) ^ IsDependencyModule.GetHashCode();

            foreach (var facet in Facets) {
                hashCode = (hashCode * 397) ^ facet.GetHashCode();
            }

            return hashCode;
        }
    }

    public override string ToString() => MarkerType.ToString();
}

/// <summary>
/// One attribute on a feature marker, reduced to the two shapes a generator can act on: a string
/// and a type.
/// </summary>
/// <remarks>
/// Only the first constructor argument, because a marker attribute states one fact -
/// <c>[TemplateContentType("text/html")]</c>, <c>[TemplateBase(typeof(HardenedHtmlTemplate&lt;&gt;))]</c>.
/// An attribute needing more than that is describing something other than a feature switch.
/// </remarks>
public class FeatureFacet : IEquatable<FeatureFacet> {
    public FeatureFacet(string name, string? value, ITypeDefinition? typeValue) {
        Name = name;
        Value = value;
        TypeValue = typeValue;
    }

    /// <summary>The attribute's name without the <c>Attribute</c> suffix.</summary>
    public string Name { get; }

    /// <summary>Its first constructor argument when that is a string, otherwise null.</summary>
    public string? Value { get; }

    /// <summary>Its first constructor argument when that is a <c>typeof</c>, otherwise null.</summary>
    public ITypeDefinition? TypeValue { get; }

    public bool Equals(FeatureFacet? other) =>
        other != null &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) &&
        string.Equals(Value, other.Value, StringComparison.Ordinal) &&
        Equals(TypeValue, other.TypeValue);

    public override bool Equals(object obj) => Equals(obj as FeatureFacet);

    public override int GetHashCode() {
        unchecked {
            var hashCode = Name.GetHashCode();

            hashCode = (hashCode * 397) ^ (Value?.GetHashCode() ?? 0);
            hashCode = (hashCode * 397) ^ (TypeValue?.GetHashCode() ?? 0);

            return hashCode;
        }
    }

    public override string ToString() => Name;
}
