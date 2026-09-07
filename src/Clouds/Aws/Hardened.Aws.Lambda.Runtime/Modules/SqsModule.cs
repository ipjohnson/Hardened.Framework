using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Adapters;
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
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IPayloadAdapter, SqsAdapter>();
    }
}
