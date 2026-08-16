namespace Hardened.Idl.Models;

/// <summary>
/// One entry of a <c>discriminator.mapping</c>: the value that appears on the wire, and the schema
/// it selects.
/// </summary>
internal class DiscriminatorMappingModel : IEquatable<DiscriminatorMappingModel> {
    public string Value { get; set; } = "";

    public string Ref { get; set; } = "";

    public bool Equals(DiscriminatorMappingModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Value == other.Value && Ref == other.Ref;
    }

    public override bool Equals(object? obj) => Equals(obj as DiscriminatorMappingModel);

    public override int GetHashCode() {
        unchecked {
            return (Value.GetHashCode() * 397) ^ Ref.GetHashCode();
        }
    }
}
