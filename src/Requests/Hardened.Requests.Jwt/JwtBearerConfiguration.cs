using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Requests.Jwt;

/// <summary>
/// Which tokens the JWT bearer source accepts, and where it reads the keys that sign them.
/// </summary>
/// <remarks>
/// <para>
/// Each value comes from an environment variable, and an application can amend any of them:
/// </para>
/// <code>
/// config.Amend((JwtBearerConfiguration jwt) => jwt.Audience = "todos-api");
/// </code>
/// <para>
/// <see cref="Issuer"/> and <see cref="Audience"/> are required, and so is one of
/// <see cref="Authority"/> and <see cref="JwksUrl"/>. The application refuses to start without
/// them, rather than refusing every token at the first request.
/// </para>
/// </remarks>
[ConfigurationModel]
public partial class JwtBearerConfiguration
{
    /// <summary>The <c>iss</c> a token must carry.</summary>
    [FromEnvironmentVariable("JWT_ISSUER")]
    private string _issuer = "";

    /// <summary>The <c>aud</c> a token must carry, or one of them when it carries several.</summary>
    [FromEnvironmentVariable("JWT_AUDIENCE")]
    private string _audience = "";

    /// <summary>
    /// An OpenID Connect issuer, whose <c>/.well-known/openid-configuration</c> names the JWKS. Used
    /// in place of <see cref="JwksUrl"/>.
    /// </summary>
    [FromEnvironmentVariable("JWT_AUTHORITY")]
    private string _authority = "";

    /// <summary>The JWKS the signing keys are read from, when there is no <see cref="Authority"/>.</summary>
    [FromEnvironmentVariable("JWT_JWKS_URL")]
    private string _jwksUrl = "";

    /// <summary>
    /// The claim a caller's grants are read from. Its value is split on spaces, and an array claim
    /// gives one grant per element, so <c>scope</c>, <c>scp</c> and <c>roles</c> all read the usual
    /// way.
    /// </summary>
    [FromEnvironmentVariable("JWT_GRANT_CLAIM")]
    private string _grantClaim = "scope";

    /// <summary>How far a token's <c>exp</c> and <c>nbf</c> may be off, in seconds.</summary>
    [FromEnvironmentVariable("JWT_CLOCK_SKEW_SECONDS")]
    private int _clockSkewSeconds = 60;
}
