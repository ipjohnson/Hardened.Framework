using System.Collections.Generic;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// What the published document says about itself: <c>info</c> and the declared security schemes.
/// </summary>
/// <remarks>
/// <para>
/// Carried into <see cref="OpenApiDocumentGenerator.Write"/> by the specification-first path,
/// merged across every spec the project declares. Null members fall back exactly as before this
/// existed: the entry point's class name, "1.0.0", and no schemes - which is also what code-first
/// gets unless it declares <c>[OpenApiInfo]</c>.
/// </para>
/// <para>
/// Value-equal, because it rides an incremental provider: an identity that compared by reference
/// would re-emit the document on every pass, and one that compared too loosely would serve a
/// stale title after the contract renamed itself.
/// </para>
/// </remarks>
public sealed class DocumentIdentity : System.IEquatable<DocumentIdentity> {

    public DocumentIdentity(
        string? title, string? version, string? description,
        IReadOnlyList<(string Name, string Json)> securitySchemes) {
        Title = title;
        Version = version;
        Description = description;
        SecuritySchemes = securitySchemes;
    }

    public string? Title { get; }

    public string? Version { get; }

    public string? Description { get; }

    /// <summary>Each scheme's component name and its OpenAPI JSON, ordered by name.</summary>
    public IReadOnlyList<(string Name, string Json)> SecuritySchemes { get; }

    public bool Equals(DocumentIdentity? other) {
        if (other is null) {
            return false;
        }

        if (Title != other.Title || Version != other.Version || Description != other.Description ||
            SecuritySchemes.Count != other.SecuritySchemes.Count) {
            return false;
        }

        for (var i = 0; i < SecuritySchemes.Count; i++) {
            if (SecuritySchemes[i] != other.SecuritySchemes[i]) {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as DocumentIdentity);

    public override int GetHashCode() {
        unchecked {
            var hash = Title?.GetHashCode() ?? 0;
            hash = (hash * 397) ^ (Version?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ SecuritySchemes.Count;
            return hash;
        }
    }
}
