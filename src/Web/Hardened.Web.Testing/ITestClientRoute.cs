namespace Hardened.Web.Testing;

/// <summary>
/// How a package teaches the harness to build a whole shape of client.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITestClientFactory{TClient}"/> answers for one client and lives in the test project.
/// This answers for every client a generator produces and lives in that generator's own testing
/// package, which is what makes a second service in the same solution cost nothing: a Kiota client
/// is recognisable by its shape, so the route builds any of them.
/// </para>
/// <para>
/// A test assembly opts in by naming the route in an assembly attribute - <c>[assembly:
/// KiotaTesting]</c> - rather than the harness finding it. Discovery would have to load every
/// referenced assembly to look, and a route silently applying to a client the test project meant to
/// build its own way is the failure that would follow. An explicit <see cref="ITestClientFactory{TClient}"/>
/// still wins for the client it names.
/// </para>
/// </remarks>
public interface ITestClientRoute {

    /// <summary>Whether this route knows how to build <paramref name="clientType"/>.</summary>
    bool CanBuild(Type clientType);

    /// <summary>
    /// Builds the client. What it runs over is <paramref name="context"/>'s to give: the harness's
    /// own <see cref="HttpClient"/>, or a fresh one over handlers of the route's choosing.
    /// </summary>
    object Build(TestClientContext context, Type clientType);
}
