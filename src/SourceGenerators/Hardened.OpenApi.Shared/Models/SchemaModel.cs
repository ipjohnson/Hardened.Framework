namespace Hardened.OpenApi.SourceGenerator.Models;

internal class SchemaModel : IEquatable<SchemaModel> {
    public string Name { get; set; } = "";
    public SchemaKind Kind { get; set; }

    /// <summary>The spec's <c>deprecated</c>, which becomes <c>[Obsolete]</c>.</summary>
    public bool IsDeprecated { get; set; }

    /// <summary>The schema's <c>description</c>, as the generated type's doc comment.</summary>
    public string? Description { get; set; }
    public List<PropertyModel> Properties { get; set; } = new();
    public List<string> EnumValues { get; set; } = new();
    public List<string> Required { get; set; } = new();
    public string? ArrayItemsRef { get; set; }
    public string? ArrayItemsType { get; set; }
    public string? ArrayItemsFormat { get; set; }
    public string? DictionaryValueType { get; set; }
    public string? DictionaryValueRef { get; set; }
    public string? Type { get; set; }
    public string? Format { get; set; }

    /// <summary>
    /// The property carrying the type discriminator, from <c>discriminator.propertyName</c>. Set on
    /// the base of a polymorphic hierarchy and null everywhere else.
    /// </summary>
    public string? DiscriminatorPropertyName { get; set; }

    /// <summary>
    /// Discriminator value to schema reference, in document order.
    /// </summary>
    /// <remarks>
    /// A list rather than a dictionary because the serialized form has to be byte-stable - the
    /// build task compares content to decide whether to rewrite the model file, and a reordered
    /// mapping would make every build look dirty.
    /// </remarks>
    public List<DiscriminatorMappingModel> DiscriminatorMapping { get; set; } = new();

    /// <summary>
    /// The schema this one derives from, as a reference. Set from an <c>allOf</c> branch pointing at
    /// a schema that carries a discriminator.
    /// </summary>
    public string? BaseRef { get; set; }

    /// <summary>
    /// Whether this schema is the base of a hierarchy, which is what makes it abstract and gives it
    /// polymorphism metadata in the generated resolver.
    /// </summary>
    public bool IsPolymorphicBase => DiscriminatorPropertyName != null;

    public bool Equals(SchemaModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && Kind == other.Kind &&
               Description == other.Description && IsDeprecated == other.IsDeprecated &&
               Type == other.Type && Format == other.Format &&
               ArrayItemsRef == other.ArrayItemsRef &&
               ArrayItemsType == other.ArrayItemsType &&
               ArrayItemsFormat == other.ArrayItemsFormat &&
               DictionaryValueType == other.DictionaryValueType &&
               DictionaryValueRef == other.DictionaryValueRef &&
               DiscriminatorPropertyName == other.DiscriminatorPropertyName &&
               BaseRef == other.BaseRef &&
               DiscriminatorMapping.SequenceEqual(other.DiscriminatorMapping) &&
               Properties.SequenceEqual(other.Properties) &&
               EnumValues.SequenceEqual(other.EnumValues) &&
               Required.SequenceEqual(other.Required);
    }

    public override bool Equals(object? obj) => Equals(obj as SchemaModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();
            hash = (hash * 397) ^ Kind.GetHashCode();
            return hash;
        }
    }
}

internal enum SchemaKind {
    Object,
    Enum,
    Array,
    Primitive,
    Dictionary
}
