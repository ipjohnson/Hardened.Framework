using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

/// <summary>
/// Where a test's application runs, and how the harness reaches it.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline host is the default: the chain runs in the test's own call, with no socket.
/// A socket host - <c>[KestrelRuntime]</c>, <c>[AspNetCoreRuntime]</c> on the test - starts a real server over the
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
    /// Whether a request here runs against a container of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The deployment model the host stands for, rather than a preference. An execution environment
    /// is not promised between invocations, so the Lambda hosts and the in-process pipeline answer
    /// <see cref="TestContainerPolicy.PerInvocation"/>, and a handler that leaned on what a previous
    /// request left behind fails here rather than in production.
    /// </para>
    /// <para>
    /// A socket host answers <see cref="TestContainerPolicy.Reused"/>, and that is forced rather
    /// than chosen: the test holds an <see cref="HttpClient"/> bound to the loopback port the kernel
    /// picked, so restarting the server per request would leave every client the harness handed out
    /// addressing a closed socket. It is a limit on what those hosts can check rather than a claim
    /// that sharing is safe on them. A service behind a load balancer is many instances, and state
    /// crossing requests is as unsafe there as it is on Lambda; run the same handlers under
    /// <see cref="PipelineHostAttribute"/> for that check.
    /// </para>
    /// </remarks>
    TestContainerPolicy ContainerPolicy => TestContainerPolicy.Reused;

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

    /// <summary>
    /// The same, for a caller whose requests are meant to reach one container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What <c>[Shared]</c> on a client parameter reaches. Pinning the parameter alone would hand
    /// the test one client object and change nothing about where its requests go, because the host
    /// decides that - so the mark has to arrive here, at the point the client is built.
    /// </para>
    /// <para>
    /// A host that reuses anyway ignores it, which is why this defaults to the plain overload
    /// rather than being something every host has to answer.
    /// </para>
    /// </remarks>
    HttpMessageHandler CreateHandler(TestCredential? credential, bool reuseContainer) =>
        CreateHandler(credential);

    /// <summary>One request, as <see cref="ITestWebApp"/> sends it.</summary>
    Task<TestWebResponse> SendAsync(TestHostRequest request, CancellationToken cancellationToken);

    /// <summary>The same, for a caller whose requests are meant to reach one container.</summary>
    Task<TestWebResponse> SendAsync(
        TestHostRequest request, CancellationToken cancellationToken, bool reuseContainer) =>
        SendAsync(request, cancellationToken);
}

/// <summary>
/// Whether a host runs each request against its own container.
/// </summary>
public enum TestContainerPolicy {

    /// <summary>One container for every request the host serves.</summary>
    Reused,

    /// <summary>A container per request, built from the test's own composition.</summary>
    PerInvocation
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
