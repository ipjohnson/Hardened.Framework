namespace Hardened.Generation.Models;

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
        get => _typeBaseName.Length > 0 ? _typeBaseName : Generation.NamingHelper.ToPascalCase(Tag ?? "Default");
        set => _typeBaseName = value ?? "";
    }

    private string _typeBaseName = "";

    /// <summary>
    /// The request header carrying <see cref="OperationModel.DispatchKey"/>, or null when this
    /// service's operations are routed by path and method.
    /// </summary>
    /// <remarks>
    /// On the service rather than on the whole document, because a Smithy model may declare several
    /// services and each carries its own protocol trait. The header is the protocol's -
    /// <c>X-Amz-Target</c> for awsJson - and it is carried as a name rather than inferred, so the
    /// routing table can emit the lookup without knowing which protocol asked for it.
    /// </remarks>
    public string? DispatchHeader { get; set; }

    public List<OperationModel> Operations { get; set; } = new();

    public bool Equals(ServiceModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Tag != other.Tag) return false;
        if (DispatchHeader != other.DispatchHeader) return false;
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
