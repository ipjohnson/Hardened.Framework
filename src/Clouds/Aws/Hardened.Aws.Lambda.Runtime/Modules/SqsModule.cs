using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.Runtime.Modules;

/// <summary>
/// Registers the SQS adapter, applied to an application as <c>[SqsModule]</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A module rather than an unconditional registration, and that is what makes it trimmable.</b>
/// Nothing in this assembly references <see cref="SqsAdapter"/> except this module, and nothing
/// references this module until an application applies it - so a function that handles no queue
/// carries no SQS adapter, no <c>SqsSerializerContext</c>, and no
/// <c>Amazon.Lambda.SQSEvents.dll</c>, because the linker finds nothing rooting them. That only
/// holds while each adapter keeps its own serializer context: one shared context would root every
/// event type in the package and leave the modules deciding nothing.
/// </para>
/// <para>
/// An application does not normally write this. <c>[Queue]</c> on a handler is what selects it,
/// through the <c>HardenedQueueModule</c> build property this package declares - which is also how
/// the same handler reaches a different adapter on a different provider.
/// </para>
/// </remarks>
[DependencyModule]
public partial class SqsModule : IServiceCollectionConfiguration {
    /// <summary>
    /// Whether the event source mapping was deployed with <c>ReportBatchItemFailures</c>, letting a
    /// failed message be returned on its own instead of failing the whole invocation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nullable, and every module property here has to be: DependencyModules generates the module
    /// attribute with each property defaulting to <c>default(T)</c> and copies it across guarded by
    /// a null check <em>only for a nullable one</em>. A non-nullable <c>bool</c> would be assigned
    /// false by <c>[SqsModule]</c> written with no arguments, which is the same value but would
    /// silently overwrite anything set another way later.
    /// </para>
    /// <para>
    /// It has to match the deployment. Reporting failures to a mapping that did not ask for them
    /// means the report is discarded and every failed message is marked handled, so this is off
    /// until something says otherwise.
    /// </para>
    /// </remarks>
    public bool? ReportBatchItemFailures { get; set; }

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IPayloadAdapter>(
            new SqsAdapter(ReportBatchItemFailures ?? false));

        services.AddBatchExecutionFilter();
    }
}
