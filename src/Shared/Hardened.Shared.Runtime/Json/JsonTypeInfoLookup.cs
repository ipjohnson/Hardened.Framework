using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Hardened.Shared.Runtime.Json;

/// <summary>
/// The metadata a serializer needs to read or write a type without reflecting over it.
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
public static class JsonTypeInfoLookup {

    /// <summary>Metadata for a statically known type.</summary>
    public static JsonTypeInfo<T> For<T>(JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

    /// <summary>
    /// Metadata for a value whose type is only known at run time — a handler's return value, which
    /// the pipeline carries as <c>object</c> because a filter may replace it.
    /// </summary>
    public static JsonTypeInfo For(JsonSerializerOptions options, object value) =>
        options.GetTypeInfo(value.GetType());

    /// <summary>
    /// Every registered resolver, in registration order, ahead of reflection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the whole point, and getting it backwards is silent. A
    /// <see cref="DefaultJsonTypeInfoResolver"/> answers for very nearly every type, so a chain that
    /// opens with one never consults anything after it: a registered
    /// <c>JsonSerializerContext</c> is walked past, and the application serializes by reflection
    /// while appearing to be configured. That is how a context carrying
    /// <c>UseStringEnumConverter</c> still wrote <c>{"category":0}</c>.
    /// </para>
    /// <para>
    /// It only shows on a JIT host. Under a trimmed or AOT publish
    /// <see cref="JsonSerializer.IsReflectionEnabledByDefault"/> is false, nothing is appended, and
    /// the context answers because it is the only thing in the chain - so production was right and
    /// the tests covering it were wrong, which is the direction that gets found last.
    /// </para>
    /// <para>
    /// Reflection is appended whenever it is available, rather than only onto an empty chain. A
    /// context is almost never total - it knows the models the application declared and not
    /// <c>string</c>, <c>DateTimeOffset</c>, or a framework error body - so "resolvers exist" is not
    /// a reason to withhold the fallback.
    /// </para>
    /// </remarks>
    public static JsonSerializerOptions WithResolvers(
        JsonSerializerOptions options, IEnumerable<IJsonTypeInfoResolver> resolvers) {
        foreach (var resolver in resolvers) {
            options.TypeInfoResolverChain.Add(resolver);
        }

        return AppendReflectionFallback(options);
    }

    /// <summary>
    /// Reflection at the end of the chain, whatever else is already in it.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="WithReflectionFallback"/>, which installs reflection only onto an
    /// empty chain. That guard reads as strictness - an application that registered a context gets
    /// told about a type it forgot - but it cannot be stated that way, because it also silently
    /// withholds reflection for <c>string</c> and every other type no context declares. This one
    /// makes no such trade: it is for the serializers whose contract is reflection.
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Guarded on JsonSerializer.IsReflectionEnabledByDefault; the trimmer removes the branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Guarded on JsonSerializer.IsReflectionEnabledByDefault; the trimmer removes the branch.")]
    public static JsonSerializerOptions AppendReflectionFallback(JsonSerializerOptions options) {
        // Idempotent, because the options a serializer is handed may be shared and several
        // serializers may pass them through here. Checked against the chain rather than against
        // TypeInfoResolver, which any resolver at all makes non-null.
        if (JsonSerializer.IsReflectionEnabledByDefault &&
            !options.TypeInfoResolverChain.Any(r => r is DefaultJsonTypeInfoResolver)) {
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        }

        return options;
    }

    /// <summary>
    /// Puts reflection at the end of the chain, where reflection is available at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything now resolves through <see cref="For{T}"/>, and a <see cref="JsonSerializerOptions"/>
    /// starts with no resolver at all - so without this, a plain JIT application that registered no
    /// <c>JsonSerializerContext</c> would throw where it used to work. The reflection overloads used
    /// to install this silently; the difference is that it is now visible and switchable.
    /// </para>
    /// <para>
    /// Last in the chain, so a registered context still answers first for the types it knows and
    /// reflection only covers what nothing generated metadata for. Guarded on
    /// <c>JsonSerializer.IsReflectionEnabledByDefault</c>, which the trimmer folds to false for a
    /// trimmed or AOT publish and removes with the branch - so those applications get the
    /// <c>NotSupportedException</c> naming the type instead, which is the truthful answer and points
    /// at the missing context.
    /// </para>
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Guarded on JsonSerializer.IsReflectionEnabledByDefault; the trimmer removes the branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Guarded on JsonSerializer.IsReflectionEnabledByDefault; the trimmer removes the branch.")]
    public static JsonSerializerOptions WithReflectionFallback(JsonSerializerOptions options) {
        // Idempotent, because the options a serializer is handed may be shared and several
        // serializers may pass them through here - and because an application that supplied its own
        // resolver has already answered this question.
        if (options.TypeInfoResolver is null && JsonSerializer.IsReflectionEnabledByDefault) {
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        }

        return options;
    }
}
