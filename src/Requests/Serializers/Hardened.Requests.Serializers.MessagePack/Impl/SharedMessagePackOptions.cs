using DependencyModules.Runtime.Attributes;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Serializers.MessagePack.Impl;

public interface ISharedMessagePackOptions {
    MessagePackSerializerOptions Options { get; }
}

/// <summary>
/// The options both halves of this package read, composed once.
/// </summary>
/// <remarks>
/// <para>
/// One instance, for the reason <c>SharedSerializer</c> is one: a reader and a writer that compose
/// their own resolvers apply one set of formatters on the way out and another on the way in, and a
/// model round-trips through neither.
/// </para>
/// <para>
/// <b>Registered resolvers first, then the AOT chain.</b> The chain is the same shape as the JSON
/// side's - <c>AotResponseSerializer</c> adds every registered <c>IJsonTypeInfoResolver</c> and then
/// its primitive resolver last - so an application registers an <c>IFormatterResolver</c> to answer
/// for something the generator did not write a formatter for, and it is asked first.
/// </para>
/// </remarks>
[SingletonService]
public class SharedMessagePackOptions : ISharedMessagePackOptions {
    public SharedMessagePackOptions(
        IServiceProvider serviceProvider,
        IOptions<IMessagePackSerializerConfiguration> configuration,
        IEnumerable<IFormatterResolver> resolvers) {
        var options = configuration.Value.OptionsProvider(serviceProvider);

        var registered = resolvers.ToArray();

        // Only where the application registered one. CompositeResolver.Create allocates a caching
        // resolver over the list, and composing a single-entry list around the same chain the
        // default already carries buys a level of indirection on every lookup for nothing.
        Options = registered.Length == 0
            ? options.WithResolver(
                CompositeResolver.Create(
                    [], MessagePackSerializerConfiguration.AotResolvers))
            : options.WithResolver(
                CompositeResolver.Create(
                    [],
                    [..registered, ..MessagePackSerializerConfiguration.AotResolvers]));
    }

    public MessagePackSerializerOptions Options { get; }
}
