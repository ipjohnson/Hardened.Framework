namespace Hardened.OpenApi.SourceGenerator.Models;

internal class SchemaModel : IEquatable<SchemaModel> {
    public string Name { get; set; } = "";
    public SchemaKind Kind { get; set; }
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

    public bool Equals(SchemaModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && Kind == other.Kind &&
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
