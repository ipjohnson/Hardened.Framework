using Hardened.Web.Runtime.Responses;

namespace Hardened.Web.Testing;

/// <summary>
/// What an <see cref="ITestClientRoute"/> builds a client over.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Http"/> is enough for a client that only needs somewhere to send requests.
/// <see cref="CreateHttpClient"/> is for a route that needs to see the responses itself: a
/// generated client throws its own model for a refusal and returns a bare body for a success, so
/// the status of a 201 and the <c>Location</c> that came with it reach the test only if something
/// in the client's own chain looked.
/// </para>
/// <para>
/// The credential is applied to whichever client is used, so a route never handles one. So is
/// <c>[Shared]</c>: both clients send every request to the test's own container where the
/// parameter carries it, and each request to a container of its own where it does not.
/// </para>
/// </remarks>
public sealed class TestClientContext
{
    private readonly ITestHost _host;
    private readonly TestCredential? _credential;
    private readonly bool _reuseContainer;

    internal TestClientContext(
        ITestHost host,
        TestCredential? credential,
        HttpClient http,
        bool reuseContainer
    )
    {
        _host = host;
        _credential = credential;
        _reuseContainer = reuseContainer;
        Http = http;
    }

    /// <summary>The harness's own client, running the host with the test's credential on it.</summary>
    public HttpClient Http { get; }

    /// <summary>
    /// The address a client resolves relative URLs against: one the pipeline ignores, or the one
    /// a socket host bound.
    /// </summary>
    public Uri BaseAddress => _host.BaseAddress;

    /// <summary>
    /// A client whose chain runs <paramref name="handlers"/> in order and then the host.
    /// </summary>
    /// <remarks>
    /// A separate transport from <see cref="Http"/> rather than a shared one, because a handler
    /// cannot be added to an <see cref="HttpClient"/> that already exists and forwarding into one
    /// fails: an <see cref="HttpRequestMessage"/> is marked sent on its way down the first chain,
    /// and the second refuses it.
    /// </remarks>
    public HttpClient CreateHttpClient(params DelegatingHandler[] handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        var chain = _host.CreateHandler(_credential, _reuseContainer);

        for (var index = handlers.Length - 1; index >= 0; index--)
        {
            handlers[index].InnerHandler = chain;
            chain = handlers[index];
        }

        var client = new HttpClient(chain) { BaseAddress = BaseAddress };

        _credential?.ApplyTo(client);

        return client;
    }
}
