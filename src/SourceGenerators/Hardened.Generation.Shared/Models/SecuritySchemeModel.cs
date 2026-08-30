namespace Hardened.Generation.Models;

/// <summary>
/// A named security scheme, carried as the OpenAPI JSON it publishes as.
/// </summary>
/// <remarks>
/// The JSON rather than a field per scheme kind, because the document is the only reader. The
/// authorization filters take what they need from <see cref="AuthorizationBranchModel"/>; this
/// exists so the published document can declare the scheme the service enforces, which it did not
/// - every arm of the front-end trial enforced authentication and published no
/// <c>securitySchemes</c> at all, so a generated client sent unauthenticated requests to every
/// write operation.
/// </remarks>
internal class SecuritySchemeModel : IEquatable<SecuritySchemeModel> {
    public string Name { get; set; } = "";

    /// <summary>The scheme object, as a complete OpenAPI JSON value.</summary>
    public string Json { get; set; } = "";

    public bool Equals(SecuritySchemeModel? other) =>
        other is not null && Name == other.Name && Json == other.Json;

    public override bool Equals(object? obj) => Equals(obj as SecuritySchemeModel);

    public override int GetHashCode() {
        unchecked {
            return (Name.GetHashCode() * 397) ^ Json.GetHashCode();
        }
    }
}
