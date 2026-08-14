namespace Hardened.OpenApi.SourceGenerator.Models;

/// <summary>
/// Represents a filter instance applied to an operation via x-filters.
/// Maps to a specific FilterTypeModel and carries the property overrides.
/// </summary>
internal class FilterInstanceModel : IEquatable<FilterInstanceModel> {
    public string FilterTypeName { get; set; } = "";
    public Dictionary<string, string> PropertyValues { get; set; } = new();

    public bool Equals(FilterInstanceModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (FilterTypeName != other.FilterTypeName) return false;
        if (PropertyValues.Count != other.PropertyValues.Count) return false;
        foreach (var kvp in PropertyValues) {
            if (!other.PropertyValues.TryGetValue(kvp.Key, out var otherValue) || kvp.Value != otherValue)
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as FilterInstanceModel);

    public override int GetHashCode() {
        unchecked {
            var hash = FilterTypeName.GetHashCode();
            foreach (var kvp in PropertyValues.OrderBy(k => k.Key))
                hash = (hash * 397) ^ kvp.Key.GetHashCode() ^ kvp.Value.GetHashCode();
            return hash;
        }
    }
}
