namespace Hardened.SourceGenerator.Models.Request;

/// <summary>
/// A security scheme a handler's attributes declare: its component name, its OpenAPI JSON, and
/// whether its kind can carry scopes.
/// </summary>
/// <remarks>
/// Value-equal because it rides <c>RequestHandlerModel</c>'s equality, which is an incremental
/// cache key - editing a scheme type's attribute must invalidate the document that repeats it.
/// </remarks>
public sealed class SecuritySchemeDeclaration : System.IEquatable<SecuritySchemeDeclaration> {

    public SecuritySchemeDeclaration(string name, string json, bool carriesScopes) {
        Name = name;
        Json = json;
        CarriesScopes = carriesScopes;
    }

    /// <summary>The <c>components.securitySchemes</c> key: the scheme type's name.</summary>
    public string Name { get; }

    /// <summary>The scheme object, as complete OpenAPI JSON.</summary>
    public string Json { get; }

    /// <summary>Whether requirements against this scheme may list scopes - OAuth2's privilege.</summary>
    public bool CarriesScopes { get; }

    public bool Equals(SecuritySchemeDeclaration? other) =>
        other is not null &&
        Name == other.Name && Json == other.Json && CarriesScopes == other.CarriesScopes;

    public override bool Equals(object? obj) => Equals(obj as SecuritySchemeDeclaration);

    public override int GetHashCode() {
        unchecked {
            return (Name.GetHashCode() * 397) ^ Json.GetHashCode();
        }
    }
}
