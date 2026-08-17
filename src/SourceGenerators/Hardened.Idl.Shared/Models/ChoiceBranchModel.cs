namespace Hardened.Idl.Models;

/// <summary>
/// One of the things a <c>oneOf</c> permits.
/// </summary>
/// <remarks>
/// <para>
/// A branch is a named schema or an inline type, and it has to be both because published
/// descriptions are mostly the second kind: of 338 undiscriminated choices across the corpus, 200
/// name nothing at all - <c>oneOf: [string, boolean]</c> and its variations - and another 45 mix a
/// reference with an inline type. Modelling a branch as a reference covers 28% of them and silently
/// drops the rest, which reads as a <c>oneOf</c> with one branch and produces no type.
/// </para>
/// <para>
/// The two are exclusive: <see cref="Ref"/> names a schema the document declares, and
/// <see cref="Type"/> is a type written in place. Nothing carries both.
/// </para>
/// </remarks>
internal class ChoiceBranchModel : IEquatable<ChoiceBranchModel> {

    /// <summary>The schema this branch names, or null for an inline one.</summary>
    public string? Ref { get; set; }

    /// <summary>The spec type written in place, or null for a reference.</summary>
    public string? Type { get; set; }

    /// <summary>The inline type's format, which decides what it maps to.</summary>
    public string? Format { get; set; }

    /// <summary>
    /// The branch as one string, for the model file.
    /// </summary>
    /// <remarks>
    /// A reference is written as itself; an inline type is prefixed with <c>=</c>, which a
    /// reference cannot start with because one always begins <c>#/</c>. That keeps the whole list
    /// on the existing string-list encoding rather than adding a record type and the ordering
    /// machinery that goes with it.
    /// </remarks>
    public string Encoded =>
        Ref ?? "=" + Type + (Format == null ? "" : ":" + Format);

    public static ChoiceBranchModel Decode(string encoded) {
        if (!encoded.StartsWith("=", StringComparison.Ordinal)) {
            return new ChoiceBranchModel { Ref = encoded };
        }

        var body = encoded.Substring(1);
        var colon = body.IndexOf(':');

        return colon < 0
            ? new ChoiceBranchModel { Type = Empty(body) }
            : new ChoiceBranchModel {
                Type = Empty(body.Substring(0, colon)),
                Format = Empty(body.Substring(colon + 1))
            };
    }

    private static string? Empty(string value) => value.Length == 0 ? null : value;

    public bool Equals(ChoiceBranchModel? other) =>
        other is not null && Ref == other.Ref && Type == other.Type && Format == other.Format;

    public override bool Equals(object? obj) => Equals(obj as ChoiceBranchModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Ref?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (Type?.GetHashCode() ?? 0);
            return (hash * 397) ^ (Format?.GetHashCode() ?? 0);
        }
    }
}
