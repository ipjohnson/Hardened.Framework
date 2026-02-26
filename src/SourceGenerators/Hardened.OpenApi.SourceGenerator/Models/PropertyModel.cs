namespace Hardened.OpenApi.SourceGenerator.Models;

internal class PropertyModel : IEquatable<PropertyModel> {
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public string? Format { get; set; }
    public string? Ref { get; set; }
    public bool IsArray { get; set; }
    public string? ArrayItemsRef { get; set; }
    public string? ArrayItemsType { get; set; }
    public string? ArrayItemsFormat { get; set; }
    public bool IsRequired { get; set; }
    public bool IsDictionary { get; set; }
    public string? DictionaryValueType { get; set; }
    public string? DictionaryValueRef { get; set; }
    public List<string>? EnumValues { get; set; }

    public bool Equals(PropertyModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && Type == other.Type && Format == other.Format &&
               Ref == other.Ref && IsArray == other.IsArray && IsRequired == other.IsRequired;
    }

    public override bool Equals(object? obj) => Equals(obj as PropertyModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();
            hash = (hash * 397) ^ (Type?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ IsRequired.GetHashCode();
            return hash;
        }
    }
}
