using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Kestrel.Testing;

/// <summary>
/// Runs the test's application on Kestrel, on a loopback port the kernel picks.
/// </summary>
/// <remarks>
/// <para>
/// On a method, a class or the assembly; the narrowest wins, and <c>[PipelineHost]</c> on a
/// method opts one test back to the pipeline. The server runs over the test's own container, so
/// a <c>[Mock]</c> behind a route reached over the socket is the same substitute the pipeline
/// test sees, the credential attributes travel as the two test headers they always were, and
/// <c>ITestWebApp</c>, an <c>HttpClient</c> parameter and every Kiota, Refit or hand-written
/// client parameter send to the socket with nothing written differently.
/// </para>
/// <para>
/// What a socket changes is what the wire changes: <c>TestWebResponse.Failure</c> is null because
/// an exception does not cross it, the headers are the ones Kestrel wrote, and
/// <c>LastResponse</c> is what came back. Every test carrying this binds and stops a server of
/// its own, which is why it goes on the smoke class and not on the assembly.
/// </para>
/// <para>
/// The Kestrel runtime module is loaded beside the application's, deduplicated when the
/// application already declares <c>[KestrelRuntime]</c>, so an application deployed on another
/// host, or on none, runs on Kestrel in a test.
/// </para>
/// </remarks>
public sealed class KestrelHostAttribute : TestHostAttribute, IDependencyModuleProvider {

    public IDependencyModule GetModule() => new KestrelRuntime();

    public override ITestHost CreateHost(ITestMethodContext testMethod, IServiceCollection services) =>
        new KestrelTestHost();
}
