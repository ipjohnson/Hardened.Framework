using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

/// <summary>
/// Where a test's application runs, and how the harness reaches it.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline host is the default: the chain runs in the test's own call, with no socket.
/// A socket host - <c>[KestrelHost]</c>, <c>[AspNetCoreHost]</c> - starts a real server over the
/// test's own container on a loopback port the kernel picks, and everything the test holds
/// follows it: <see cref="ITestWebApp"/> sends to the socket, the <see cref="HttpClient"/> the
/// harness hands out and every typed client built over it send to the socket, and
/// <see cref="LastResponse"/> is recorded from what came back over it.
/// </para>
/// <para>
/// One per test container, registered by <c>[WebTesting]</c> through a factory so the container
/// disposes it, started once the container exists, and disposed with the container - which is
/// when the test has run under NUnit, and when the case has run under xUnit.
/// </para>
/// </remarks>
public interface ITestHost : IAsyncDisposable {

    /// <summary>
    /// Whether an unmatched path is a 404 here, or is handed to something behind the host. The
    /// pipeline and Kestrel are terminal; the ASP.NET Core host is not, because falling through
    /// to the rest of the ASP.NET pipeline is the behaviour it exists to show.
    /// </summary>
    bool IsTerminal { get; }

    /// <summary>
    /// What a client resolves relative URLs against: <c>http://harness/</c> for the pipeline,
    /// which ignores it, or the address the server bound.
    /// </summary>
    Uri BaseAddress { get; }

    /// <summary>
    /// Runs the startup services once, composes the chain, and begins listening where there is
    /// a socket. Called once, after the container is built.
    /// </summary>
    Task StartAsync(IServiceProvider provider, CancellationToken cancellationToken);

    /// <summary>
    /// The terminal handler a client's chain ends in. <paramref name="credential"/> is applied to
    /// a request that carries neither test header.
    /// </summary>
    HttpMessageHandler CreateHandler(TestCredential? credential);

    /// <summary>One request, as <see cref="ITestWebApp"/> sends it.</summary>
    Task<TestWebResponse> SendAsync(TestHostRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// What <see cref="ITestWebApp"/> hands a host: the method, the path and query as a client would
/// put them on the wire, the headers, the body as bytes, and the credential to apply where the
/// headers carry neither test header.
/// </summary>
public sealed record TestHostRequest(
    string Method,
    string PathAndQuery,
    IDictionary<string, StringValues> Headers,
    Stream Body,
    TestCredential? Credential);
