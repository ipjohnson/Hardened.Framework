namespace Hardened.Idl.Models;

/// <summary>
/// Represents a filter attribute type defined in x-filter-types.
/// The source generator emits a partial attribute class from this definition.
/// </summary>
internal class FilterTypeModel : IEquatable<FilterTypeModel> {
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public List<FilterTypePropertyModel> Properties { get; set; } = new();

    /// <summary>
    /// When false, the type is defined in an external library and should not
    /// be generated. The x-filter-types entry is used only for mapping
    /// x-filters instances to the correct type. Default: true.
    /// </summary>
    public bool Generate { get; set; } = true;

    /// <summary>
    /// The generated class name: PascalCase(Name) + "Attribute".
    /// </summary>
    public string ClassName => NamingHelper.ToPascalCase(Name) + "Attribute";

    /// <summary>
    /// Fully qualified type name: Namespace.ClassName.
    /// </summary>
    public string FullTypeName => $"{Namespace}.{ClassName}";

    public bool Equals(FilterTypeModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Name != other.Name || Namespace != other.Namespace) return false;
        return Properties.SequenceEqual(other.Properties);
    }

    public override bool Equals(object? obj) => Equals(obj as FilterTypeModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();
            hash = (hash * 397) ^ Namespace.GetHashCode();
            foreach (var p in Properties) hash = (hash * 397) ^ p.GetHashCode();
            return hash;
        }
    }
}

/// <summary>
/// A single property on a filter attribute type.
/// </summary>
internal class FilterTypePropertyModel : IEquatable<FilterTypePropertyModel> {
    public string Name { get; set; } = "";
    public string CSharpType { get; set; } = "string";
    public string? Default { get; set; }
    public List<string>? EnumValues { get; set; }

    /// <summary>
    /// Fully qualified C# enum type (e.g. "FriendsWithRecipes.BackEndCommon.Throttle.ThrottleMode").
    /// When set, the property type uses this instead of CSharpType, and values are emitted as enum literals.
    /// </summary>
    public string? EnumType { get; set; }

    public bool Equals(FilterTypePropertyModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && CSharpType == other.CSharpType && Default == other.Default;
    }

    public override bool Equals(object? obj) => Equals(obj as FilterTypePropertyModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();
            hash = (hash * 397) ^ CSharpType.GetHashCode();
            if (Default != null) hash = (hash * 397) ^ Default.GetHashCode();
            return hash;
        }
    }
}
