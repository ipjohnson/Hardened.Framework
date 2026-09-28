namespace Hardened.Requests.Jwt.Testing;

/// <summary>
/// What <see cref="TestJwtIssuer.Token"/> puts in a token. It starts as a token the application
/// accepts, and a test changes what it is testing.
/// </summary>
public sealed class TestJwt
{
    /// <summary>The <c>sub</c>. Null leaves it out.</summary>
    public string? Subject { get; set; } = "integration-test";

    /// <summary>
    /// The grants, written into the claim <c>JWT_GRANT_CLAIM</c> names, joined with spaces.
    /// </summary>
    public IList<string> Grants { get; set; } = [];

    /// <summary>Any other claims. A value may be a string, a number, a bool or an array of them.</summary>
    public IDictionary<string, object> Claims { get; } = new Dictionary<string, object>();

    /// <summary>The <c>iss</c>. Starts as the one the application accepts.</summary>
    public string Issuer { get; set; } = "";

    /// <summary>The <c>aud</c>. Starts as the one the application accepts.</summary>
    public string Audience { get; set; } = "";

    /// <summary>The <c>exp</c>. Null is an hour from now.</summary>
    public DateTime? Expires { get; set; }

    /// <summary>The <c>nbf</c>. Null is now, or an hour before <see cref="Expires"/> when that has passed.</summary>
    public DateTime? NotBefore { get; set; }

    /// <summary>
    /// Signs with a key the application does not trust, so the signature fails to verify.
    /// </summary>
    public bool SignedByUntrustedKey { get; set; }
}
