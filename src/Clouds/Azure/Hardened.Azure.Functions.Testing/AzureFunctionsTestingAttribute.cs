using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Functions.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Azure.Functions.Testing;

/// <summary>
/// Delivers test messages the way the isolated worker does, rather than straight into the pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Sits alongside <c>[FunctionTesting]</c> and changes one thing: what happens between a façade
/// call and the handler. The neutral delivery builds a request; this builds the trigger data the
/// worker would bind for the generated function - the <c>ServiceBusReceivedMessage[]</c> a batched
/// Service Bus trigger hands over - and a <c>FunctionContext</c> carrying the binding data the host
/// sends beside it, and goes in through <c>FunctionsInvocationHandler</c>, so the adapter, the
/// batch fan-out and the message-to-request mapping are all exercised.
/// </para>
/// <code>
/// [assembly: FunctionTesting]
/// [assembly: AzureFunctionsTesting]
/// [assembly: HardenedTestEntryPoint(typeof(OrdersApp))]
/// </code>
/// <para>
/// No test method changes when this is added or removed, which is the point: fidelity is a
/// property of the project rather than of the test, so the same tests run at either level and
/// against another provider.
/// </para>
/// <para>
/// Removes before adding rather than relying on order, as <c>[LambdaTesting]</c> does: whichever
/// of the two attributes the runner reaches first, the neutral one registers with <c>TryAdd</c>
/// and this one replaces.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class AzureFunctionsTestingAttribute : Attribute, ITestServiceSetupAttribute {
    public void SetupServiceCollection(
        ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        serviceCollection.AddTriggerTesting();

        serviceCollection.RemoveAll<ITriggerDelivery>();
        serviceCollection.AddSingleton<ITriggerDelivery, FunctionsTriggerDelivery>();
    }
}
