using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Kestrel.Testing;

/// <summary>
/// A <c>[KestrelRuntime]</c> on a test runs it on Kestrel, on a loopback port the kernel picks.
/// </summary>
/// <remarks>
/// <para>
/// One line, once, anywhere in the test project:
/// </para>
/// <code>
/// [assembly: KestrelTesting]
/// </code>
/// <para>
/// After it, the attribute an application names its host with names a test's host too: on a
/// method, a class or the assembly, the narrowest wins, and <c>[PipelineHost]</c> on a method
/// opts one test back to the pipeline. The runner loads the Kestrel runtime module beside the
/// application's, deduplicated when the application already carries it, so an application
/// deployed on another host, or on none, runs on Kestrel in a test.
/// </para>
/// <para>
/// The server runs over the test's own container, so a <c>[Mock]</c> behind a route reached over
/// the socket is the same substitute the pipeline test sees, the credential attributes travel as
/// the two test headers they always were, and <c>ITestWebApp</c>, an <c>HttpClient</c> parameter
/// and every Kiota, Refit or hand-written client parameter send to the socket with nothing
/// written differently. What a socket changes is what the wire changes:
/// <c>TestWebResponse.Failure</c> is null because an exception does not cross it, the headers
/// are the ones Kestrel wrote, and <c>LastResponse</c> is what came back. Every test carrying the
/// attribute binds and stops a server of its own, which is why it goes on the smoke class and not
/// on the assembly.
/// </para>
/// </remarks>
public sealed class KestrelTestingAttribute : TestHostProviderAttribute {

    public override Type RuntimeAttribute => typeof(KestrelRuntimeAttribute);

    public override ITestHost CreateHost(ITestMethodContext testMethod, IServiceCollection services) =>
        new KestrelTestHost();
}
