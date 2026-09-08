using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Functions.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Gcp.CloudRun.Testing;

/// <summary>
/// Delivers test messages the way Cloud Run receives them, rather than straight into the pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Sits alongside <c>[FunctionTesting]</c> and changes one thing: what happens between a façade
/// call and the handler. The neutral delivery builds a request; this builds the push Pub/Sub
/// sends and posts it to the test's web host, so the front door, the envelope, the decoded body
/// and the metadata headers are all exercised - and, on a class carrying <c>[KestrelRuntime]</c>
/// under <c>[KestrelTesting]</c>, so is the socket.
/// </para>
/// <code>
/// [assembly: WebTesting]
/// [assembly: FunctionTesting]
/// [assembly: CloudRunTesting]
/// [assembly: HardenedTestEntryPoint(typeof(OrdersApp))]
/// </code>
/// <para>
/// <c>[WebTesting]</c> has to be beside it: that is what registers and starts the host the push
/// goes to, and the delivery says so if it is missing. No test method changes when this is added
/// or removed, which is the point: fidelity is a property of the project rather than of the test,
/// so the same tests run at either level and against another provider.
/// </para>
/// <para>
/// Removes before adding rather than relying on order. Whichever of the two attributes the runner
/// reaches first, the neutral one registers with <c>TryAdd</c> and this one replaces - so both
/// orders end with the envelope delivery.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class CloudRunTestingAttribute : Attribute, ITestServiceSetupAttribute {
    public void SetupServiceCollection(
        ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        serviceCollection.AddTriggerTesting();

        serviceCollection.RemoveAll<ITriggerDelivery>();
        serviceCollection.AddSingleton<ITriggerDelivery, CloudRunEnvelopeDelivery>();
    }
}
