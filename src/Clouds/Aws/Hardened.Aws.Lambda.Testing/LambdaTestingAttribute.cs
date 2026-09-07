using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Functions.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Aws.Lambda.Testing;

/// <summary>
/// Delivers test messages the way Lambda does, rather than straight into the pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Sits alongside <c>[FunctionTesting]</c> and changes one thing: what happens between a façade
/// call and the handler. The neutral delivery builds a request; this builds the envelope the source
/// actually sends and goes in through the invocation loop, so the adapter, the peek, the body
/// encoding that source uses and its metadata are all exercised.
/// </para>
/// <code>
/// [assembly: FunctionTesting]
/// [assembly: LambdaTesting]
/// [assembly: HardenedTestEntryPoint(typeof(OrdersApp))]
/// </code>
/// <para>
/// No test method changes when this is added or removed, which is the point: fidelity is a
/// property of the project rather than of the test, so the same tests run at either level and
/// against another provider.
/// </para>
/// <para>
/// Removes before adding rather than relying on order. Whichever of the two attributes the runner
/// reaches first, the neutral one registers with <c>TryAdd</c> and this one replaces - so both
/// orders end with the envelope delivery.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class LambdaTestingAttribute : Attribute, ITestServiceSetupAttribute {
    public void SetupServiceCollection(
        ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        serviceCollection.AddTriggerTesting();

        serviceCollection.RemoveAll<ITriggerDelivery>();
        serviceCollection.AddSingleton<ITriggerDelivery, LambdaEnvelopeDelivery>();
    }
}
