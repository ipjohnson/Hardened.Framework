using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Testing;

/// <summary>
/// Names the host a test's application runs on.
/// </summary>
/// <remarks>
/// <para>
/// One per test: the narrowest declaration wins, so a class of socket tests carries
/// <c>[KestrelHost]</c> once and a method that wants the fast path opts back with
/// <see cref="PipelineHostAttribute"/>. With none in scope the pipeline host is used.
/// </para>
/// <para>
/// A host package derives from this and ships one attribute -
/// <c>Hardened.Web.Kestrel.Testing</c> ships <c>[KestrelHost]</c> - because hosting needs the
/// ASP.NET Core shared framework and a test project on the pipeline must not be forced onto it.
/// A host attribute that also implements <c>IDependencyModuleProvider</c> has its module loaded
/// beside the application's, deduplicated when the application already carries it, which is
/// what lets an application declaring another host, or none, run on a socket in a test.
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
