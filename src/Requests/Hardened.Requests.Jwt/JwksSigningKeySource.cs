using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Hardened.Requests.Jwt;

/// <summary>
/// Reads the signing keys from the issuer's JWKS, and keeps them.
/// </summary>
/// <remarks>
/// <para>
/// The JWKS is at <see cref="JwtBearerConfiguration.JwksUrl"/>, or wherever the
/// <c>jwks_uri</c> of <see cref="JwtBearerConfiguration.Authority"/>'s OpenID Connect discovery
/// document says. Both are fetched with a plain <see cref="HttpClient"/> and read with
/// <see cref="JsonDocument"/> and <see cref="JsonWebKeySet"/>, so nothing here needs reflection.
/// </para>
/// <para>
/// The keys are fetched when first asked for, and again once they are 24 hours old. A token naming
/// a key the set does not hold fetches them again, at most once a minute, which is how a rotated key
/// is picked up. A fetch that fails keeps the keys already held. With none held, it fails the
/// request, and the next minute's requests fail without asking the issuer again.
/// </para>
/// </remarks>
public sealed class JwksSigningKeySource : IJwtSigningKeySource, IDisposable
{
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(24);

    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly IJwtBearerConfiguration _configuration;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _fetching = new(1, 1);

    private KeySet? _keys;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private Exception? _lastFailure;

    public JwksSigningKeySource(IJwtBearerConfiguration configuration)
        : this(configuration, new HttpClientHandler(), TimeProvider.System) { }

    internal JwksSigningKeySource(
        IJwtBearerConfiguration configuration,
        HttpMessageHandler handler,
        TimeProvider time
    )
    {
        _configuration = configuration;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _time = time;
    }

    public async ValueTask<IReadOnlyList<SecurityKey>> GetKeys(
        string? keyId,
        CancellationToken cancellationToken
    )
    {
        var held = Volatile.Read(ref _keys);
        var now = _time.GetUtcNow();

        if (held != null && now - held.FetchedAt < RefreshAfter && held.Names(keyId))
        {
            return held.Keys;
        }

        if (now - _lastAttempt < RetryAfter)
        {
            return held?.Keys ?? throw Unavailable();
        }

        return (await Fetch(held, cancellationToken)).Keys;
    }

    /// <summary>
    /// The JWKS's address, checked. Used at startup as well, so a wrong setting stops the
    /// application rather than its first request.
    /// </summary>
    internal static Uri Checked(string setting, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{setting} is '{value}', which is not a URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
        {
            throw new InvalidOperationException(
                $"{setting} is '{value}'. Signing keys are read over https, except from a loopback "
                    + "address, because a key read over plain HTTP can be replaced on the way."
            );
        }

        return uri;
    }

    private async Task<KeySet> Fetch(KeySet? held, CancellationToken cancellationToken)
    {
        await _fetching.WaitAsync(cancellationToken);

        try
        {
            // Another request fetched while this one waited, or tried and failed.
            if (Volatile.Read(ref _keys) is { } fetched && !ReferenceEquals(fetched, held))
            {
                return fetched;
            }

            if (_time.GetUtcNow() - _lastAttempt < RetryAfter)
            {
                return held ?? throw Unavailable();
            }

            _lastAttempt = _time.GetUtcNow();

            try
            {
                // Not the request's token. The keys are every caller's, so one caller hanging up must
                // not leave the rest without them for a minute. The client's timeout bounds the fetch.
                var keys = new KeySet(await Download(), _time.GetUtcNow());

                _lastFailure = null;
                Volatile.Write(ref _keys, keys);

                return keys;
            }
            catch (Exception failure) when (held != null)
            {
                _lastFailure = failure;

                return held;
            }
            catch (Exception failure)
            {
                _lastFailure = failure;

                throw;
            }
        }
        finally
        {
            _fetching.Release();
        }
    }

    private InvalidOperationException Unavailable() =>
        new(
            "The JWT signing keys could not be fetched. They are asked for again a minute after "
                + "the last attempt.",
            _lastFailure
        );

    private async Task<IReadOnlyList<SecurityKey>> Download()
    {
        var jwks = await JwksUrl();

        var set = new JsonWebKeySet(await _http.GetStringAsync(jwks));

        return set.GetSigningKeys().ToArray();
    }

    private async Task<Uri> JwksUrl()
    {
        if (string.IsNullOrEmpty(_configuration.Authority))
        {
            return Checked("JWT_JWKS_URL", _configuration.JwksUrl);
        }

        var authority = Checked("JWT_AUTHORITY", _configuration.Authority);

        var discovery = new Uri(
            authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration"
        );

        using var document = JsonDocument.Parse(await _http.GetStringAsync(discovery));

        if (
            !document.RootElement.TryGetProperty("jwks_uri", out var jwksUri)
            || jwksUri.GetString() is not { Length: > 0 } value
        )
        {
            throw new InvalidOperationException(
                $"{discovery} names no jwks_uri, so the signing keys cannot be found."
            );
        }

        return Checked("jwks_uri", value);
    }

    public void Dispose()
    {
        _http.Dispose();
        _fetching.Dispose();
    }

    private sealed record KeySet(IReadOnlyList<SecurityKey> Keys, DateTimeOffset FetchedAt)
    {
        public bool Names(string? keyId) =>
            keyId is null
            || Keys.Any(key => string.Equals(key.KeyId, keyId, StringComparison.Ordinal));
    }
}
