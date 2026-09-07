using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Functions.Testing;

/// <summary>
/// The trigger harness: a test declares the façade it wants and sends.
/// </summary>
/// <remarks>
/// <para>
/// The function counterpart of <c>[WebTesting]</c>, and it does far less because there is far less
/// to do - no host to resolve, no credential, no typed client. A trigger test needs the façades
/// resolvable and a delivery behind them, so this registers both and stops.
/// </para>
/// <code>
/// [assembly: NSubstituteSupport]
/// [assembly: FunctionTesting]
/// [assembly: HardenedTestEntryPoint(typeof(OrdersApp))]
///
/// [HardenedTest]
/// public async Task AnOrderIsPlaced(
///     IQueuesOf&lt;OrdersApp.Queues&gt; queues, [Mock] IOrderStore store) {
///     await queues.SendTo.OrdersNew(new Order { Id = "a-1" });
///
///     store.Received().Place(Arg.Is&lt;Order&gt;(order =&gt; order.Id == "a-1"));
/// }
/// </code>
/// <para>
/// The delivery it registers builds a request and runs the pipeline, which names no cloud. A
/// project wanting the provider's own envelope exercised adds that provider's testing attribute
/// alongside; the test method does not change, only the fidelity behind it.
/// </para>
/// <para>
/// Registration attributes run after the application's own modules, which is what lets a test
/// substitute a collaborator the handler resolves - <c>[Mock]</c> replaces it rather than competing
/// with it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public class FunctionTestingAttribute : Attribute, ITestServiceSetupAttribute {
    public void SetupServiceCollection(
        ITestMethodContext testMethod, IServiceCollection serviceCollection) =>
        serviceCollection.AddTriggerTesting();
}
