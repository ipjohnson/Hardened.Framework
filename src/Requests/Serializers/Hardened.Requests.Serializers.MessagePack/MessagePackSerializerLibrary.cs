using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Serializers.MessagePack;

/// <summary>
/// Registers the MessagePack reader and writer.
/// </summary>
/// <remarks>
/// <para>
/// Importing this does not change what an operation produces. The serializer registers under
/// <see cref="MessagePackContentType.Value"/> and answers only where an operation declares that
/// media type, so a service that imports the package and declares nothing still answers JSON. That
/// is the difference from <c>Hardened.Requests.Serializers.Newtonsoft</c>, which registers under
/// <c>application/json</c> and exists to displace what is already there.
/// </para>
/// <para>
/// <b>The configuration is registered here.</b> <c>[ConfigurationModel]</c> writes the interface and
/// the implementation and nothing else: something has to hand the implementation to the
/// configuration manager and bridge it to <c>IOptions&lt;T&gt;</c>, which is what
/// <c>HardenedRequestModule</c> does for the four it owns. Without it, resolving
/// <c>IOptions&lt;IMessagePackSerializerConfiguration&gt;</c> asks <c>OptionsFactory</c> to
/// construct an interface and the container throws on the first request.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HardenedModule]
/// [MessagePackSerializerLibrary]
/// public partial class MyApplication { }
///
/// [Get("/todos/{id}")]
/// [Produces(KnownContentType.Json, MessagePackContentType.Value)]
/// public Todo Get(int id) => ...;
/// </code>
/// </example>
[DependencyModule]
public partial class MessagePackSerializerLibrary : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                new IConfigurationValueProvider[] {
                    new NewConfigurationValueProvider<
                        IMessagePackSerializerConfiguration, MessagePackSerializerConfiguration>(null)
                }));

        services.AddSingleton(
            provider => Options.Create(
                provider.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<IMessagePackSerializerConfiguration>()));
    }
}
