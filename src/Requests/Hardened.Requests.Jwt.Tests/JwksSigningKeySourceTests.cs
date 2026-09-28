using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Hardened.Requests.Jwt.Tests;

/// <summary>
/// Where the keys are read from, how long they are kept, and when they are fetched again.
/// </summary>
public class JwksSigningKeySourceTests
{
    private const string Jwks = "https://issuer.test/keys";

    /// <summary>Answers each URL with a body, counts the requests, and can be told to fail.</summary>
    private sealed class Issuer : HttpMessageHandler
    {
        public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);

        public List<string> Requested { get; } = [];

        public bool Down { get; set; }

        /// <summary>When set, a request waits for it, as it would for a slow issuer.</summary>
        public TaskCompletionSource? Answer { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var url = request.RequestUri!.AbsoluteUri;

            Requested.Add(url);

            if (Answer is { } answer)
            {
                await answer.Task.WaitAsync(cancellationToken);
            }

            if (Down || !Bodies.TryGetValue(url, out var body))
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string KeySet(params string[] keyIds) =>
        JsonSerializer.Serialize(
            new
            {
                keys = keyIds.Select(keyId =>
                {
                    var parameters = RSA.Create(2048).ExportParameters(false);

                    return new
                    {
                        kty = "RSA",
                        use = "sig",
                        kid = keyId,
                        n = Base64UrlEncoder.Encode(parameters.Modulus),
                        e = Base64UrlEncoder.Encode(parameters.Exponent),
                    };
                }),
            }
        );

    private static (JwksSigningKeySource Keys, Issuer Issuer, Clock Clock) Build(
        string jwksUrl = Jwks,
        string authority = ""
    )
    {
        var issuer = new Issuer();
        var clock = new Clock();

        var configuration = new JwtBearerConfiguration { JwksUrl = jwksUrl, Authority = authority };

        return (new JwksSigningKeySource(configuration, issuer, clock), issuer, clock);
    }

    private static async Task<string?[]> KeyIds(JwksSigningKeySource keys, string? keyId = null) =>
        (await keys.GetKeys(keyId, TestContext.Current.CancellationToken))
            .Select(key => key.KeyId)
            .ToArray();

    [Fact]
    public async Task TheKeysAreReadFromTheJwks()
    {
        var (keys, issuer, _) = Build();

        issuer.Bodies[Jwks] = KeySet("k1");

        Assert.Equal(["k1"], await KeyIds(keys));
    }

    [Fact]
    public async Task TheAuthoritysDiscoveryDocumentNamesTheJwks()
    {
        var (keys, issuer, _) = Build(jwksUrl: "", authority: "https://issuer.test/tenant/");

        issuer.Bodies["https://issuer.test/tenant/.well-known/openid-configuration"] =
            """{"issuer":"https://issuer.test/tenant/","jwks_uri":"https://issuer.test/tenant/keys"}""";
        issuer.Bodies["https://issuer.test/tenant/keys"] = KeySet("k1");

        Assert.Equal(["k1"], await KeyIds(keys));
    }

    [Fact]
    public async Task ADiscoveryDocumentWithNoJwksFails()
    {
        var (keys, issuer, _) = Build(jwksUrl: "", authority: "https://issuer.test");

        issuer.Bodies["https://issuer.test/.well-known/openid-configuration"] =
            """{"issuer":"https://issuer.test"}""";

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => KeyIds(keys));

        Assert.Contains("jwks_uri", failure.Message);
    }

    [Fact]
    public async Task TheKeysAreKept()
    {
        var (keys, issuer, clock) = Build();

        issuer.Bodies[Jwks] = KeySet("k1");

        await KeyIds(keys);
        clock.Now = clock.Now.AddHours(23);
        await KeyIds(keys, "k1");

        Assert.Single(issuer.Requested);
    }

    [Fact]
    public async Task TheKeysAreFetchedAgainAfterADay()
    {
        var (keys, issuer, clock) = Build();

        issuer.Bodies[Jwks] = KeySet("k1");
        await KeyIds(keys);

        issuer.Bodies[Jwks] = KeySet("k2");
        clock.Now = clock.Now.AddHours(24);

        Assert.Equal(["k2"], await KeyIds(keys));
        Assert.Equal(2, issuer.Requested.Count);
    }

    /// <summary>
    /// A rotated key is picked up when a token names it, but a stream of tokens naming a key that
    /// does not exist asks the issuer at most once a minute.
    /// </summary>
    [Fact]
    public async Task AnUnknownKeyFetchesTheKeysAgainAtMostOnceAMinute()
    {
        var (keys, issuer, clock) = Build();

        issuer.Bodies[Jwks] = KeySet("k1");
        await KeyIds(keys);

        issuer.Bodies[Jwks] = KeySet("k1", "k2");

        Assert.Equal(["k1"], await KeyIds(keys, "k2"));

        clock.Now = clock.Now.AddMinutes(1);

        Assert.Equal(["k1", "k2"], await KeyIds(keys, "k2"));
        Assert.Equal(2, issuer.Requested.Count);

        clock.Now = clock.Now.AddSeconds(30);
        await KeyIds(keys, "k3");

        Assert.Equal(2, issuer.Requested.Count);
    }

    [Fact]
    public async Task AFailedFetchKeepsTheKeysAlreadyHeld()
    {
        var (keys, issuer, clock) = Build();

        issuer.Bodies[Jwks] = KeySet("k1");
        await KeyIds(keys);

        issuer.Down = true;
        clock.Now = clock.Now.AddHours(25);

        Assert.Equal(["k1"], await KeyIds(keys));
        Assert.Equal(2, issuer.Requested.Count);
    }

    /// <summary>
    /// With no keys held, a failure fails the request, and the requests of the next minute fail
    /// without asking the issuer again.
    /// </summary>
    [Fact]
    public async Task WithNoKeysAFailureIsNotRetriedForAMinute()
    {
        var (keys, issuer, clock) = Build();

        issuer.Down = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => KeyIds(keys));

        var unavailable = await Assert.ThrowsAsync<InvalidOperationException>(() => KeyIds(keys));

        Assert.IsType<HttpRequestException>(unavailable.InnerException);
        Assert.Single(issuer.Requested);

        issuer.Down = false;
        issuer.Bodies[Jwks] = KeySet("k1");
        clock.Now = clock.Now.AddMinutes(1);

        Assert.Equal(["k1"], await KeyIds(keys));
    }

    /// <summary>
    /// The keys are every caller's, so the caller whose request started the fetch hanging up does
    /// not cancel it. With no keys held, a cancelled fetch would fail every request of the next
    /// minute.
    /// </summary>
    [Fact]
    public async Task ACallerHangingUpDoesNotCancelTheFetch()
    {
        var (keys, issuer, _) = Build();
        using var hangsUp = new CancellationTokenSource();

        issuer.Bodies[Jwks] = KeySet("k1");
        issuer.Answer = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        var first = keys.GetKeys(null, hangsUp.Token);

        await hangsUp.CancelAsync();
        issuer.Answer.SetResult();
        await first;

        Assert.Equal(["k1"], await KeyIds(keys));
        Assert.Single(issuer.Requested);
    }

    [Theory]
    [InlineData("https://issuer.test/keys")]
    [InlineData("http://localhost:8080/keys")]
    [InlineData("http://127.0.0.1/keys")]
    public void AnHttpsOrLoopbackAddressIsAccepted(string url)
    {
        Assert.Equal(new Uri(url), JwksSigningKeySource.Checked("JWT_JWKS_URL", url));
    }

    [Theory]
    [InlineData("http://issuer.test/keys")]
    [InlineData("issuer.test/keys")]
    public void AnyOtherAddressIsRefused(string url)
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            JwksSigningKeySource.Checked("JWT_JWKS_URL", url)
        );

        Assert.Contains("JWT_JWKS_URL", failure.Message);
    }
}
