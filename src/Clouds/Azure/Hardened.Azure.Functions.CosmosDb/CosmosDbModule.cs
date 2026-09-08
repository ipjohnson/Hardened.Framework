using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Modules;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.CosmosDb;

/// <summary>
/// Registers the Cosmos DB change feed adapter, applied to an application as
/// <c>[CosmosDbModule(Database = "...")]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Selected by <c>[Change]</c> on a handler through the <c>HardenedChangeModule</c> build property,
/// which is also how the same handler reaches DynamoDB Streams on Lambda. Unlike that adapter this
/// one always needs a line on the application: <c>[Change("orders")]</c> names a container, and a
/// container lives in a database the neutral trigger has no slot for. That is a deployment fact,
/// so it is a module property, and a change handler with no database named is HRDAZ003 at build.
/// </para>
/// <para>
/// The lease container is the extension's own bookkeeping of where each partition's feed was
/// read to. It defaults to the extension's <c>leases</c>, and the generated function asks the
/// extension to create it, so a fresh database works.
/// </para>
/// </remarks>
[DependencyModule]
[FunctionsRuntimeModule]
public partial class CosmosDbModule : IServiceCollectionConfiguration {
    /// <summary>The database every <c>[Change]</c> handler's container lives in. Required.</summary>
    public string? Database { get; set; }

    /// <summary>
    /// The app setting that holds the Cosmos connection, or null for the extension's default,
    /// <c>CosmosDB</c>.
    /// </summary>
    public string? Connection { get; set; }

    /// <summary>The lease container's name, or null for the extension's default.</summary>
    public string? LeaseContainer { get; set; }

    /// <summary>
    /// How many times the host invokes a function again after a failed invocation, or null for
    /// no retry. Written with <see cref="RetryDelay"/>, or not at all.
    /// </summary>
    /// <remarks>
    /// The extension checkpoints the lease after each call, failed or not, so a retry policy is
    /// the one way a thrown batch is delivered again; see <see cref="CosmosDbAdapter"/>. The
    /// generator writes the two properties as the worker's <c>[FixedDelayRetry]</c> on every
    /// change function and into the metadata the host indexes.
    /// </remarks>
    public int? RetryCount { get; set; }

    /// <summary>
    /// The wait between one attempt and the next, as <c>hh:mm:ss</c>, or null for no retry.
    /// Written with <see cref="RetryCount"/>, or not at all.
    /// </summary>
    public string? RetryDelay { get; set; }

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerAdapter>(new CosmosDbAdapter());

        services.AddBatchExecutionFilter();
    }

    /// <summary>By type alone, so applying the module twice loads one adapter.</summary>
    public override bool Equals(object? obj) => obj is CosmosDbModule;

    public override int GetHashCode() => typeof(CosmosDbModule).GetHashCode();
}
