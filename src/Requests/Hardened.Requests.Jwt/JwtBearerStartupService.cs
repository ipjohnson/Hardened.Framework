using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Jwt;

/// <summary>
/// Checks the JWT configuration and fetches the signing keys, before the first request.
/// </summary>
/// <remarks>
/// <para>
/// A missing issuer, audience or key address stops the application, because every token would be
/// refused and nothing would say why.
/// </para>
/// <para>
/// A key fetch that fails only logs a warning. The issuer may be down for a moment, and taking the
/// application down with it would refuse the requests that need no token as well. The keys are
/// fetched again when a token needs them, at most once a minute.
/// </para>
/// </remarks>
internal sealed class JwtBearerStartupService : IStartupService
{
    public async Task<bool> Startup(IServiceProvider rootProvider)
    {
        var configuration = rootProvider
            .GetRequiredService<IOptions<IJwtBearerConfiguration>>()
            .Value;
        var keys = rootProvider.GetRequiredService<IJwtSigningKeySource>();

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.Issuer))
        {
            missing.Add("JWT_ISSUER is not set.");
        }

        if (string.IsNullOrWhiteSpace(configuration.Audience))
        {
            missing.Add("JWT_AUDIENCE is not set.");
        }

        if (
            keys is JwksSigningKeySource
            && string.IsNullOrEmpty(configuration.Authority)
            && string.IsNullOrEmpty(configuration.JwksUrl)
        )
        {
            missing.Add(
                "Neither JWT_AUTHORITY nor JWT_JWKS_URL is set, so there is nowhere to read the "
                    + "JWT signing keys from. Set JWT_AUTHORITY to the issuer's URL, or "
                    + "JWT_JWKS_URL to its JWKS."
            );
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                string.Join(" ", missing)
                    + " The JWT bearer source accepts no token until "
                    + (missing.Count == 1 ? "it is set." : "they are set.")
            );
        }

        if (keys is JwksSigningKeySource)
        {
            if (!string.IsNullOrEmpty(configuration.Authority))
            {
                JwksSigningKeySource.Checked("JWT_AUTHORITY", configuration.Authority);
            }
            else
            {
                JwksSigningKeySource.Checked("JWT_JWKS_URL", configuration.JwksUrl);
            }
        }

        try
        {
            await keys.GetKeys(null, CancellationToken.None);
        }
        catch (Exception failure)
        {
            rootProvider
                .GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(JwtBearerStartupService).FullName!)
                .LogWarning(
                    failure,
                    "The JWT signing keys could not be fetched at startup. A request that carries a "
                        + "token fails until they are, and they are asked for again a minute after "
                        + "this attempt."
                );
        }

        return true;
    }
}
