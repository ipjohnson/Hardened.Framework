using Hardened.Shared.Runtime.Attributes;
using MessagePack;
using MessagePack.Resolvers;

namespace Hardened.Requests.Serializers.MessagePack;

/// <summary>
/// How this package configures MessagePack.
/// </summary>
/// <remarks>
/// A factory over the service provider rather than a built options object, matching
/// <c>NewtonsoftSerializerConfiguration</c>: an application that wants its own resolver usually
/// wants one that was constructed with something from the container.
/// </remarks>
[ConfigurationModel]
public partial class MessagePackSerializerConfiguration {
    private Func<IServiceProvider, MessagePackSerializerOptions> _optionsProvider = DefaultOptions();

    /// <summary>
    /// The resolvers every composition ends with, ahead of nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No <c>StandardResolver</c>, and no dynamic resolver of any kind.</b> The tail of
    /// <c>StandardResolver</c> is <c>DynamicObjectResolver</c>, which emits a formatter at run time
    /// for any type that reached it - so a model nobody annotated serializes on a developer's
    /// machine and throws <c>PlatformNotSupportedException</c> after a NativeAOT publish. That is
    /// the failure <c>AotResponseSerializer</c> refuses a reflection resolver to avoid, said in the
    /// same words: the one configuration where the mistake is invisible must not be the one the
    /// developer builds in.
    /// </para>
    /// <para>
    /// <see cref="SourceGeneratedFormatterResolver"/> is what answers for an application's own
    /// models. MessagePack's source generator writes a formatter for every type carrying
    /// <c>[MessagePackObject]</c>, and this resolver finds them, so a model that is annotated needs
    /// no registration and a model that is not fails the same way everywhere.
    /// </para>
    /// </remarks>
    public static IFormatterResolver[] AotResolvers { get; } = [
        SourceGeneratedFormatterResolver.Instance,
        BuiltinResolver.Instance,
        AttributeFormatterResolver.Instance,
        DynamicGenericResolver.Instance,
        PrimitiveObjectResolver.Instance
    ];

    private static Func<IServiceProvider, MessagePackSerializerOptions> DefaultOptions() {
        return _ => MessagePackSerializerOptions.Standard;
    }
}
