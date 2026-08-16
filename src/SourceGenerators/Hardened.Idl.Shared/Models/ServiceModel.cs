namespace Hardened.Idl.Models;

internal class ServiceModel : IEquatable<ServiceModel> {
    public string Tag { get; set; } = "";

    /// <summary>
    /// The C# name the service interface and controller are built from.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Tag"/> for the same reason an operation's is: the tag is the
    /// document's, and two tags may differ only in ways C# naming removes.
    /// </remarks>
    public string TypeBaseName {
        get => _typeBaseName.Length > 0 ? _typeBaseName : Idl.NamingHelper.ToPascalCase(Tag ?? "Default");
        set => _typeBaseName = value ?? "";
    }

    private string _typeBaseName = "";
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
