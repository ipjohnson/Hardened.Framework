using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Testing;

/// <summary>
/// Names, for the assembly's tests, the host a runtime attribute on a test stands for.
/// </summary>
/// <remarks>
/// <para>
/// An application names where it deploys with its runtime attribute - <c>[KestrelRuntime]</c>,
/// <c>[AspNetCoreRuntime]</c> - and a test names where it runs with the same attribute: on a
/// method, a class or the assembly, the narrowest wins, and <see cref="PipelineHostAttribute"/>
/// opts one back to the pipeline. The runtime package cannot know about the harness, so the test
/// project says which testing package answers for which attribute, once, in an assembly
/// attribute the package ships - <c>[assembly: KestrelTesting]</c>,
/// <c>[assembly: AspNetCoreTesting]</c> - the way <c>[assembly: KiotaTesting]</c> names a client
/// route. A runtime attribute on a test that no provider answers for loads its module, as the
/// runner always did, and the test stays on the pipeline.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public abstract class TestHostProviderAttribute : Attribute {

    /// <summary>The runtime attribute this provider answers for, on a test.</summary>
    public abstract Type RuntimeAttribute { get; }

    /// <summary>
    /// The host for one test. Called once per container, before it is built, so the host can
    /// register what it needs. <c>[WebTesting]</c> registers the instance and starts it once the
    /// container exists.
    /// </summary>
    public abstract ITestHost CreateHost(ITestMethodContext testMethod, IServiceCollection services);
}
