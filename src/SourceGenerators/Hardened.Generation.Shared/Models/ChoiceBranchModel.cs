namespace Hardened.Generation.Models;

/// <summary>
/// One branch of a <see cref="SchemaKind.OneOf"/> schema or of a property that offers a choice.
/// </summary>
internal class ChoiceBranchModel : IEquatable<ChoiceBranchModel> {

    public string? Ref { get; set; }

    public string? Type { get; set; }

    public string? Format { get; set; }

    /// <summary>
    /// The name the description gives this branch, or null where it gives none.
    /// </summary>
    /// <remarks>
    /// A Smithy union names every member, and a streamed union writes that name as the
    /// <c>event:</c> field beside each item, which is what tells a client which member arrived. An
    /// OpenAPI <c>oneOf</c> lists bare schemas and has nothing to put here.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// The branch as one string, for the intermediate file: a reference, or <c>=type:format</c>,
    /// with <c>|name</c> after either when the branch is named.
    /// </summary>
    public string Encoded =>
        (Ref ?? "=" + Type + (Format == null ? "" : ":" + Format)) + (Name == null ? "" : "|" + Name);

    public static ChoiceBranchModel Decode(string encoded) {
        string? name = null;
        var bar = encoded.LastIndexOf('|');

        if (bar >= 0) {
            name = Empty(encoded.Substring(bar + 1));
            encoded = encoded.Substring(0, bar);
        }

        if (!encoded.StartsWith("=", StringComparison.Ordinal)) {
            return new ChoiceBranchModel { Ref = encoded, Name = name };
        }

        var body = encoded.Substring(1);
        var colon = body.IndexOf(':');

        return colon < 0
            ? new ChoiceBranchModel { Type = Empty(body), Name = name }
            : new ChoiceBranchModel {
                Type = Empty(body.Substring(0, colon)),
                Format = Empty(body.Substring(colon + 1)),
                Name = name
            };
    }

    private static string? Empty(string value) => value.Length == 0 ? null : value;

    public bool Equals(ChoiceBranchModel? other) =>
        other is not null && Ref == other.Ref && Type == other.Type && Format == other.Format &&
        Name == other.Name;

    public override bool Equals(object? obj) => Equals(obj as ChoiceBranchModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Ref?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (Type?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (Format?.GetHashCode() ?? 0);
            return (hash * 397) ^ (Name?.GetHashCode() ?? 0);
        }
    }
}
