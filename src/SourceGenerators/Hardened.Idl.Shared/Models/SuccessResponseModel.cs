namespace Hardened.Idl.Models;

/// <summary>
/// One 2xx response the specification declares.
/// </summary>
/// <remarks>
/// <para>
/// A sibling of <see cref="ErrorResponseModel"/>, and it exists for the same reason that one does:
/// the parser used to collapse the set. It looped over every 2xx and assigned
/// <c>SuccessStatusCode</c> and the flat payload fields on each pass, so a document declaring 200
/// and 202 generated only the 202 - last write wins, in ascending status order, with no diagnostic.
/// The data was already in hand and was overwritten.
/// </para>
/// <para>
/// <b>The flat fields on <c>OperationModel</c> stay</b> and keep naming the primary success, which
/// is the lowest declared 2xx. Standard mode returns one type and every existing consumer reads
/// those individually, so this list is additive rather than a replacement - nothing that worked
/// before has to learn about it.
/// </para>
/// <para>
/// <b>Why a second success cannot be left out.</b> Standard mode reaches its other statuses by
/// throwing, and a throw carries a failure. There is no way to throw a 202. So an operation with
/// more than one declared success has to state them in its return type whatever mode the module is
/// in, which is why <c>ServiceInterfaceEmitter</c> returns a response set for these regardless.
/// </para>
/// <para>
/// Two schemas at one status are not two entries here. That is a <c>oneOf</c>, which the schema
/// layer already models and <c>OneOfEmitter</c> already emits - and it has to be, because two cases
/// of one status would give a union two conversions the compiler cannot tell apart.
/// </para>
/// </remarks>
internal class SuccessResponseModel : IEquatable<SuccessResponseModel> {
    public int StatusCode { get; set; }

    /// <summary>The declared body's schema, or null where the response has no content.</summary>
    /// <remarks>
    /// Null is 204's ordinary state rather than an absence to work around. A case carrying no body
    /// is already modelled end to end - <c>CaseType</c> has <c>hasBody</c>, and the emitted dispatch
    /// writes <c>ShouldSerialize = false</c> - so the only thing that was missing was a branch to
    /// carry it.
    /// </remarks>
    public string? Ref { get; set; }

    /// <summary>The body's type where it is a primitive rather than a <c>$ref</c>.</summary>
    /// <remarks>
    /// Carried because <c>$ref</c> alone was not enough one layer down either: only the ref was
    /// kept for array items, so <c>items: {type: string}</c> had nothing to name and every
    /// array-of-primitives response became <c>JsonElement</c>. Array-of-<c>$ref</c> worked, which is
    /// why it went unnoticed. Same shape of omission, same fix.
    /// </remarks>
    public string? Type { get; set; }

    /// <summary>The body's <c>format</c>, which distinguishes int32 from int64 and date from date-time.</summary>
    public string? Format { get; set; }

    public bool IsArray { get; set; }

    /// <summary>The array's element schema, where the elements are a <c>$ref</c>.</summary>
    public string? ArrayItemsRef { get; set; }

    /// <summary>The array's element type, where the elements are primitives.</summary>
    public string? ArrayItemsType { get; set; }

    public string? ContentType { get; set; }

    public string? Description { get; set; }

    public bool Equals(SuccessResponseModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return StatusCode == other.StatusCode &&
               Ref == other.Ref &&
               Type == other.Type &&
               Format == other.Format &&
               IsArray == other.IsArray &&
               ArrayItemsRef == other.ArrayItemsRef &&
               ArrayItemsType == other.ArrayItemsType &&
               ContentType == other.ContentType &&
               Description == other.Description;
    }

    public override bool Equals(object? obj) => Equals(obj as SuccessResponseModel);

    public override int GetHashCode() {
        unchecked {
            var hash = StatusCode * 397;
            hash = (hash * 397) ^ (Ref?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (Type?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (ArrayItemsRef?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
