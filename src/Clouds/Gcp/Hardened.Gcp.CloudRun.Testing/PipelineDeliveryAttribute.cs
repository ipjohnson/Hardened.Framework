using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Functions.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Gcp.CloudRun.Testing;

/// <summary>
/// Opts a class or a method back to the neutral delivery inside an assembly that declared
/// <see cref="CloudRunTestingAttribute"/>: the message is built as a request and the pipeline runs,
/// with no envelope and no host, the way a project that references no cloud testing package
/// delivers.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>[PipelineHost]</c> in Hardened.Web.Testing, and what lets one test
/// project hold a fixture's tests at every rung: the assembly runs through the envelope, and one
/// class carrying this runs the same façade calls through the pipeline alone.
/// </para>
/// <para>
/// Names no cloud, and belongs in Hardened.Functions.Testing beside <c>[FunctionTesting]</c>,
/// which is where SCR-204 asks for it to go; it lives here until then so the Google fixtures can
/// use it. A class attribute runs after the assembly's, so removing and re-adding here wins
/// whichever order the assembly attributes ran in.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PipelineDeliveryAttribute : Attribute, ITestServiceSetupAttribute {
    public void SetupServiceCollection(ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        serviceCollection.RemoveAll<ITriggerDelivery>();
        serviceCollection.AddSingleton<ITriggerDelivery, PipelineDelivery>();
    }
}
