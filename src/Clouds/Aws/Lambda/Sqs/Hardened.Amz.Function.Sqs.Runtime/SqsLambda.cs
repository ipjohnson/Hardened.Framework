using DependencyModules.Runtime.Interfaces;
using Hardened.Amz.Function.Lambda.Runtime.Filter;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Sqs.Runtime;

/// <summary>
/// The SQS batch handling, applied to an application as <c>[SqsLambda]</c>.
///
/// <para>
/// Registers through <see cref="IServiceCollectionConfiguration"/>. It previously declared a
/// <c>RegisterDependencies</c> method, which DependencyModules does not call — it looks for
/// <c>ConfigureServices</c> on this interface, as every other module in this repository
/// implements. So <see cref="IBatchProcessorExceptionHandler"/> was never registered by applying
/// the module, and any application using it failed to resolve <c>SqsBatchFilter</c> at startup.
/// The runtime's own tests called <c>RegisterDependencies</c> directly, which is why they passed
/// over it. Found and fixed 2026-08-12 while restoring the integration harness, which is the only
/// thing that composes this module the way a consumer does.
/// </para>
/// </summary>
[HardenedModule]
public partial class SqsLambda : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton<IBatchProcessorExceptionHandler, BatchProcessorExceptionHandler>();
    }
}