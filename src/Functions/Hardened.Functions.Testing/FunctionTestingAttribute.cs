using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Functions.Testing;

/// <summary>
/// The trigger harness: a test declares the façade it wants and sends.
/// </summary>
/// <remarks>
/// <para>
/// The function counterpart of <c>[WebTesting]</c>, and it does far less because there is far less
/// to do - no host to resolve, no credential, no typed client.
/// </para>
/// <code>
/// [assembly: NSubstituteSupport]
/// [assembly: FunctionTesting]
/// [assembly: HardenedTestEntryPoint(typeof(OrdersApp))]
///
/// [HardenedTest]
/// public async Task AnOrderIsPlaced(OrdersApp.Queues queues, [Mock] IOrderStore store) {
///     await queues.OrdersNew(new Order { Id = "a-1" });
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
        ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        serviceCollection.AddTriggerTesting();

        RegisterFacadeParameters(testMethod, serviceCollection);
    }

    /// <summary>
    /// An instance for every parameter that is a façade.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same arrangement <c>[WebTesting]</c> uses for typed clients: this attribute already sees
    /// the test method when it sets up the collection, so it registers what it can build and
    /// ordinary resolution does the rest - no new hook in the runner.
    /// </para>
    /// <para>
    /// A façade is recognised by its constructor taking <c>TriggerSend</c> or <c>TriggerCall</c>,
    /// which are named delegates rather than <c>Func</c> shapes. That is the whole of the rule:
    /// nothing has such a constructor by accident, so nothing is constructed by accident. A
    /// parameter with its own value provider - <c>[Mock]</c>, for one - is left to it, and so is a
    /// type the container already knows.
    /// </para>
    /// </remarks>
    private static void RegisterFacadeParameters(
        ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        foreach (var parameter in testMethod.Method.GetParameters()) {
            var type = parameter.ParameterType;

            if (type == typeof(IServiceProvider) ||
                parameter.GetCustomAttributes(inherit: true).OfType<ITestParameterValueProvider>().Any() ||
                serviceCollection.Any(descriptor => descriptor.ServiceType == type) ||
                TriggerInvoker.Constructor(type) == null) {
                continue;
            }

            serviceCollection.AddScoped(
                type, provider => provider.GetRequiredService<TriggerInvoker>().Facade(type));
        }
    }
}
