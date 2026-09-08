using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Functions.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.IntegrationTests.CloudRunQueue.SUT.Tests;

/// <summary>
/// Opts a class back to the neutral delivery inside an assembly that declared
/// <c>[CloudRunTesting]</c>: the message is built as a request and the pipeline runs, with no
/// envelope and no host, the way a project that references no cloud testing package delivers.
/// </summary>
/// <remarks>
/// The counterpart of <c>[PipelineHost]</c> in Hardened.Web.Testing, and local to this project
/// until Hardened.Functions.Testing carries one. A class attribute runs after the assembly's, so
/// removing and re-adding here wins whichever order the assembly attributes ran in.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PipelineDeliveryAttribute : Attribute, ITestServiceSetupAttribute {
    public void SetupServiceCollection(ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        serviceCollection.RemoveAll<ITriggerDelivery>();
        serviceCollection.AddSingleton<ITriggerDelivery, PipelineDelivery>();
    }
}
