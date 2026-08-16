namespace Hardened.Idl.Models;

internal class ServiceModel : IEquatable<ServiceModel> {
    public string Tag { get; set; } = "";
    public List<OperationModel> Operations { get; set; } = new();

    public bool Equals(ServiceModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Tag != other.Tag) return false;
        if (Operations.Count != other.Operations.Count) return false;
        for (var i = 0; i < Operations.Count; i++) {
            if (!Operations[i].Equals(other.Operations[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ServiceModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Tag.GetHashCode();
            foreach (var op in Operations) hash = (hash * 397) ^ op.GetHashCode();
            return hash;
        }
    }
}
