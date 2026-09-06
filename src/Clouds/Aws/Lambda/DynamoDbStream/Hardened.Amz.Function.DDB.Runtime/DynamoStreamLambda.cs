using DependencyModules.Runtime.Interfaces;
using Hardened.Amz.Function.Lambda.Runtime.Filter;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.DDB.Runtime;

/// <summary>
/// The DynamoDB stream handling, applied to an application as <c>[DynamoStreamLambda]</c>.
///
/// <para>
/// Registers through <see cref="IServiceCollectionConfiguration"/>. It previously declared a
/// <c>RegisterDependencies</c> method, which DependencyModules does not call — see the note on
/// <c>SqsLambda</c>, which carried the same defect. Fixed 2026-08-12.
/// </para>
/// </summary>
[HardenedModule]
public partial class DynamoStreamLambda : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton<IBatchProcessorExceptionHandler, BatchProcessorExceptionHandler>();
    }
}