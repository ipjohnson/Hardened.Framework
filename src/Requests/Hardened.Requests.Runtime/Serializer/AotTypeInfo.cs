using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Hardened.Requests.Runtime.Serializer;

/// <summary>
/// The metadata a serializer needs to write a type without reflecting over it.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Aot*</c> serializers took a <see cref="JsonSerializerOptions"/> and handed it to
/// <c>JsonSerializer.SerializeAsync(stream, value, options)</c>, which is the reflection-based
/// overload — annotated <c>RequiresUnreferencedCode</c> and <c>RequiresDynamicCode</c> in the BCL.
/// It reads the resolver chain when one is present, so it worked, and it would have kept working
/// right up until a trimmer removed a type nothing statically referenced. The classes were named
/// for a guarantee they did not make.
/// </para>
/// <para>
/// <see cref="JsonSerializerOptions.GetTypeInfo(System.Type)"/> is the same lookup without the
/// annotations: it consults the <c>TypeInfoResolverChain</c> those serializers already populate
/// from every registered <see cref="IJsonTypeInfoResolver"/>, and hands back metadata a
/// source-generated context produced. Passing that to <c>JsonSerializer</c> is what makes the call
/// analyzable.
/// </para>
/// <para>
/// It throws when nothing in the chain knows the type, and that is the point. Reflection made the
/// same case succeed on a JIT and fail after publishing; failing in both says the application is
/// missing a <c>JsonSerializerContext</c> for the type, which is a fact about its configuration
/// rather than about its host.
/// </para>
/// </remarks>
internal static class AotTypeInfo {

    /// <summary>Metadata for a statically known type.</summary>
    internal static JsonTypeInfo<T> For<T>(JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

    /// <summary>
    /// Metadata for a value whose type is only known at run time — a handler's return value, which
    /// the pipeline carries as <c>object</c> because a filter may replace it.
    /// </summary>
    internal static JsonTypeInfo For(JsonSerializerOptions options, object value) =>
        options.GetTypeInfo(value.GetType());
}
