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

    // Validation constraints
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public bool ExclusiveMinimum { get; set; }
    public bool ExclusiveMaximum { get; set; }
    public string? Pattern { get; set; }
    public int? MinItems { get; set; }
    public int? MaxItems { get; set; }
    public List<string>? EnumValues { get; set; }

    public bool HasValidationConstraints =>
        IsRequired || MinLength.HasValue || MaxLength.HasValue ||
        Minimum.HasValue || Maximum.HasValue ||
        ExclusiveMinimum || ExclusiveMaximum ||
        Pattern != null || MinItems.HasValue || MaxItems.HasValue ||
        EnumValues is { Count: > 0 };

    public bool Equals(ParameterModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && In == other.In && IsRequired == other.IsRequired &&
               Type == other.Type && Format == other.Format &&
               MinLength == other.MinLength && MaxLength == other.MaxLength &&
               Minimum == other.Minimum && Maximum == other.Maximum &&
               ExclusiveMinimum == other.ExclusiveMinimum && ExclusiveMaximum == other.ExclusiveMaximum &&
               Pattern == other.Pattern && MinItems == other.MinItems && MaxItems == other.MaxItems;
    }

    public override bool Equals(object? obj) => Equals(obj as ParameterModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();
            hash = (hash * 397) ^ In.GetHashCode();
            hash = (hash * 397) ^ IsRequired.GetHashCode();
            hash = (hash * 397) ^ (MinLength?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (MaxLength?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (Pattern?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
