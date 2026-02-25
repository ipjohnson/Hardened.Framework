namespace Hardened.OpenApi.SourceGenerator.Models;

internal class ParameterModel : IEquatable<ParameterModel> {
    public string Name { get; set; } = "";
    public string In { get; set; } = "";
    public bool IsRequired { get; set; }
    public string? Type { get; set; }
    public string? Format { get; set; }
    public string? Ref { get; set; }
    public bool IsArray { get; set; }
    public string? ArrayItemsType { get; set; }
    public string? ArrayItemsRef { get; set; }

    public bool Equals(ParameterModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && In == other.In && IsRequired == other.IsRequired &&
               Type == other.Type && Format == other.Format;
    }

    public override bool Equals(object? obj) => Equals(obj as ParameterModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();
            hash = (hash * 397) ^ In.GetHashCode();
            hash = (hash * 397) ^ IsRequired.GetHashCode();
            return hash;
        }
    }
}
