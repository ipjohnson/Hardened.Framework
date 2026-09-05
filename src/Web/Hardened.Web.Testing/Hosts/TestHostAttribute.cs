using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Testing;

/// <summary>
/// Names a host for a test explicitly, where the runtime attributes do not.
/// </summary>
/// <remarks>
/// <para>
/// Hardened's own hosts are named by the runtime attribute the application uses:
/// <c>[KestrelRuntime]</c> or <c>[AspNetCoreRuntime]</c> on a method, a class or the assembly,
/// with the testing package that answers for it named once in
/// <see cref="TestHostProviderAttribute"/>. This is the explicit form beside that: the narrowest
/// declaration of either kind wins, and <see cref="PipelineHostAttribute"/> is the one Hardened
/// ships, for opting a method back to the pipeline inside a class that runs on a socket.
/// </para>
/// <para>
/// A host of a consumer's own derives from this: one attribute, one host, and a module loaded
/// beside the application's when the attribute also implements <c>IDependencyModuleProvider</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false)]
public abstract class TestHostAttribute : Attribute {

    /// <summary>
    /// The host for one test. Called once per container, before it is built, so the host can
    /// register what it needs. <c>[WebTesting]</c> registers the instance and starts it once the
    /// container exists.
    /// </summary>
    public abstract ITestHost CreateHost(ITestMethodContext testMethod, IServiceCollection services);
}
