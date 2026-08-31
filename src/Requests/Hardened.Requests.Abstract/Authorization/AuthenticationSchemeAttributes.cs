namespace Hardened.Requests.Abstract.Authorization;

/// <summary>Where an API key travels.</summary>
public enum ApiKeyLocation {
    Header,
    Query,
    Cookie
}

/// <summary>The OAuth2 flow a scheme declares.</summary>
public enum OAuth2Flow {
    AuthorizationCode,
    ClientCredentials,
    Implicit,
    Password
}

/// <summary>
/// Declares an <see cref="IAuthenticationScheme"/> type as an HTTP authentication scheme -
/// <c>bearer</c>, <c>basic</c>, <c>digest</c>.
/// </summary>
/// <remarks>
/// The arguments become the scheme's <c>components.securitySchemes</c> entry, keyed by the
/// declaring type's name. An HTTP scheme cannot carry scopes, so grants required beside it reach
/// the document as "authenticated via this scheme" and nothing more - the same rule the OpenAPI
/// reader applies in the other direction.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class HttpAuthenticationSchemeAttribute : Attribute {
    public HttpAuthenticationSchemeAttribute(string scheme) {
        Scheme = scheme;
    }

    /// <summary>The RFC 9110 auth-scheme token: <c>bearer</c>, <c>basic</c>, <c>digest</c>.</summary>
    public string Scheme { get; }

    /// <summary>A hint for documentation - <c>JWT</c>, typically.</summary>
    public string? BearerFormat { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// Declares an <see cref="IAuthenticationScheme"/> type as an API-key scheme.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ApiKeyAuthenticationSchemeAttribute : Attribute {
    public ApiKeyAuthenticationSchemeAttribute(string name, ApiKeyLocation location) {
        Name = name;
        Location = location;
    }

    /// <summary>The header, query or cookie name the key travels in.</summary>
    public string Name { get; }

    public ApiKeyLocation Location { get; }

    public string? Description { get; set; }
}

/// <summary>
/// Declares an <see cref="IAuthenticationScheme"/> type as an OAuth2 scheme with one flow.
/// </summary>
/// <remarks>
/// OAuth2 is the scheme kind that carries scopes, so grants required beside it become the
/// requirement's scope list in the published document. The flow's scope catalogue is emitted
/// empty for now; the scopes an operation actually requires appear on the operation.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class OAuth2AuthenticationSchemeAttribute : Attribute {
    public OAuth2AuthenticationSchemeAttribute(OAuth2Flow flow) {
        Flow = flow;
    }

    public OAuth2Flow Flow { get; }

    public string? AuthorizationUrl { get; set; }

    public string? TokenUrl { get; set; }

    public string? RefreshUrl { get; set; }

    public string? Description { get; set; }
}
