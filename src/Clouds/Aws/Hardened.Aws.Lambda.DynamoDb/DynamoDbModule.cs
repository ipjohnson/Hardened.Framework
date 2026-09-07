using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Modules;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.DynamoDb;

/// <summary>
/// Registers the DynamoDB Streams adapter, applied to an application as <c>[DynamoDbModule]</c>.
/// </summary>
/// <remarks>
/// An application does not normally write this. <c>[Change]</c> on a handler is what selects it,
/// through the <c>HardenedChangeModule</c> build property this package declares - which is also how
/// the same handler reaches a Cosmos change feed on another provider.
/// </remarks>
[DependencyModule]
[LambdaRuntimeModule]
public partial class DynamoDbModule : IServiceCollectionConfiguration {
    /// <summary>
    /// Whether the event source mapping was deployed with <c>ReportBatchItemFailures</c>.
    /// </summary>
    /// <remarks>
    /// Nullable, as every module property here has to be: DependencyModules copies a property
    /// across guarded by a null check only for a nullable one, so a non-nullable bool would be
    /// assigned false by <c>[DynamoDbModule]</c> written with no arguments.
    /// </remarks>
    public bool? ReportBatchItemFailures { get; set; }

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IPayloadAdapter>(
            new DynamoDbAdapter(ReportBatchItemFailures ?? false));

        services.AddBatchExecutionFilter();
    }

    /// <summary>
    /// By type alone, so applying the module twice loads one adapter.
    /// </summary>
    public override bool Equals(object? obj) => obj is DynamoDbModule;

    public override int GetHashCode() => typeof(DynamoDbModule).GetHashCode();
}
