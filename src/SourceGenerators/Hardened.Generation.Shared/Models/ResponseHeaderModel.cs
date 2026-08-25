namespace Hardened.Generation.Models;

/// <summary>
/// One header a response declares it carries.
/// </summary>
/// <remarks>
/// <para>
/// The declaration and the value are different facts, and only the first is a contract. A document
/// says <c>201 carries a Location</c>; it cannot say what that Location is, because the value is
/// derived from the resource the handler just made. So this becomes a constructor parameter on the
/// generated case type and the handler supplies it - the same division <c>Created&lt;T&gt;</c>
/// already makes, where the type carries the header and the caller carries its value.
/// </para>
/// <para>
/// <b>No schema type.</b> A header is a string on the wire whatever the document types it as, and
/// an <c>integer</c> header still has to be formatted by whoever knows the units. Emitting
/// <c>int</c> here would buy one conversion and cost the ability to send <c>"3"</c> quoted for an
/// ETag, which is the header that motivated this.
/// </para>
/// </remarks>
internal class ResponseHeaderModel : IEquatable<ResponseHeaderModel> {

    /// <summary>The header's name, as it goes on the wire.</summary>
    public string Name { get; set; } = "";

    /// <summary>The identifier the generated constructor parameter carries.</summary>
    /// <remarks>
    /// Held rather than derived at every use, because deriving it twice is how a parameter and the
    /// property that reads it end up spelled differently. <c>X-Rate-Limit</c> has no legal C#
    /// spelling of its own, so the name on the wire and the name in the signature must both be
    /// carried.
    /// </remarks>
    public string ParameterName { get; set; } = "";

    public string? Description { get; set; }

    public bool Equals(ResponseHeaderModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Name == other.Name &&
               ParameterName == other.ParameterName &&
               Description == other.Description;
    }

    public override bool Equals(object? obj) => Equals(obj as ResponseHeaderModel);

    public override int GetHashCode() {
        unchecked {
            return (Name.GetHashCode() * 397) ^ ParameterName.GetHashCode();
        }
    }
}
