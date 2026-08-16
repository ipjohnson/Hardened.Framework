namespace Hardened.Idl.Models;

internal class ServiceSpecModel : IEquatable<ServiceSpecModel> {
    public string FileName { get; set; } = "";
    public List<SchemaModel> Schemas { get; set; } = new();
    public List<ServiceModel> Services { get; set; } = new();
    public List<FilterTypeModel> FilterTypes { get; set; } = new();

    /// <summary>
    /// What the build task called this spec's <c>IJsonTypeInfoResolver</c>.
    /// </summary>
    /// <remarks>
    /// Carried across rather than recomputed, so the routing table registers the name that was
    /// actually emitted. Deriving it independently on both sides is how the two would drift.
    /// </remarks>
    public string JsonTypeInfoResolverName { get; set; } = "";

    /// <summary>
    /// What the build task emitted for validation, per operation. Empty when nothing is constrained.
    /// </summary>
    public List<ValidatedOperationModel> ValidatedOperations { get; set; } = new();

    public bool Equals(ServiceSpecModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (FileName != other.FileName) return false;
        if (JsonTypeInfoResolverName != other.JsonTypeInfoResolverName) return false;
        if (Schemas.Count != other.Schemas.Count) return false;
        if (Services.Count != other.Services.Count) return false;
        if (FilterTypes.Count != other.FilterTypes.Count) return false;
        if (ValidatedOperations.Count != other.ValidatedOperations.Count) return false;

        for (var i = 0; i < ValidatedOperations.Count; i++) {
            if (!ValidatedOperations[i].Equals(other.ValidatedOperations[i])) return false;
        }

        for (var i = 0; i < Schemas.Count; i++) {
            if (!Schemas[i].Equals(other.Schemas[i])) return false;
        }

        for (var i = 0; i < Services.Count; i++) {
            if (!Services[i].Equals(other.Services[i])) return false;
        }

        for (var i = 0; i < FilterTypes.Count; i++) {
            if (!FilterTypes[i].Equals(other.FilterTypes[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ServiceSpecModel);

    public override int GetHashCode() {
        unchecked {
            var hash = FileName.GetHashCode();
            foreach (var s in Schemas) hash = (hash * 397) ^ s.GetHashCode();
            foreach (var s in Services) hash = (hash * 397) ^ s.GetHashCode();
            foreach (var f in FilterTypes) hash = (hash * 397) ^ f.GetHashCode();
            return hash;
        }
    }
}
