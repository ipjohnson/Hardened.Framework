namespace Hardened.OpenApi.SourceGenerator.Models;

/// <summary>
/// What the build task emitted for one operation's validation, for the generator to wire up.
/// </summary>
/// <remarks>
/// The seam between the two halves. The task cannot name the handler's <c>Parameters</c> class - it
/// is nested inside a handler type whose name carries a computed suffix - so it names an interface
/// and records it here. The generator adds that interface to the class it does know how to name, and
/// registers the validator by the name it was given rather than deriving one and drifting.
/// </remarks>
internal class ValidatedOperationModel : IEquatable<ValidatedOperationModel> {
    /// <summary>The operation whose parameters these validate.</summary>
    public string OperationId { get; set; } = "";

    /// <summary>The interface the generated <c>Parameters</c> class implements.</summary>
    public string InterfaceName { get; set; } = "";

    public bool Equals(ValidatedOperationModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return OperationId == other.OperationId && InterfaceName == other.InterfaceName;
    }

    public override bool Equals(object? obj) => Equals(obj as ValidatedOperationModel);

    public override int GetHashCode() {
        unchecked {
            return (OperationId.GetHashCode() * 397) ^ InterfaceName.GetHashCode();
        }
    }
}
