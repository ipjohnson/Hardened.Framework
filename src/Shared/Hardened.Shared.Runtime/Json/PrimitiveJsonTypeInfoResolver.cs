using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Hardened.Shared.Runtime.Json;

/// <summary>
/// Metadata for the BCL leaf types every generated resolver's property infos bottom out in.
/// </summary>
/// <remarks>
/// <para>
/// <b>These entries are load-bearing, not a convenience.</b> A resolver chain is asked for far more
/// than the types a specification declares. <c>JsonMetadataServices.CreatePropertyInfo&lt;T&gt;</c>
/// carries no converter of its own - it resolves one when the type info is configured, by asking the
/// options, which walks the chain. So serializing a record with one <c>string</c> property asks the
/// chain for <c>string</c> as well as for the record. Nothing in System.Text.Json answers that
/// implicitly: the only built-in resolver that would is <see cref="DefaultJsonTypeInfoResolver"/>,
/// which is the reflection path this design exists to avoid. With no entry for <c>string</c> the
/// chain returns null and every string property throws <c>NotSupportedException</c>.
/// </para>
/// <para>
/// <b>They used to be emitted into every generated resolver.</b> One copy per specification file, in
/// a chain that already holds one resolver per specification file - so N copies of a table that
/// cannot vary, each a place for the set to drift. It lives here once instead. What stays generated
/// is what is actually specification-specific: the schema types, their enums and choice types, and
/// the collections whose element type is one of those.
/// </para>
/// <para>
/// <b>The set is deliberately wider than the generator currently needs.</b> <c>TypeMapper</c> maps
/// <c>uuid</c> to <c>string</c> and every <c>number</c> to <c>double</c> today, so <see cref="Guid"/>
/// and <see cref="decimal"/> are unreachable from a specification. That is a fact about one switch
/// expression, not about the format, and the failure when it changes is silent at build time and
/// throws at run time on the first payload. Covering the leaf types the BCL has converters for costs
/// one comparison each and removes the coupling.
/// </para>
/// <para>
/// Registered last in the chain by the serializers, so a hand-written
/// <see cref="JsonSerializerContext"/> that answers for one of these still wins.
/// </para>
/// </remarks>
public sealed class PrimitiveJsonTypeInfoResolver : IJsonTypeInfoResolver {

    /// <summary>
    /// The chain holds this in several places at once and it carries no state, so there is one.
    /// </summary>
    public static readonly PrimitiveJsonTypeInfoResolver Instance = new();

    private PrimitiveJsonTypeInfoResolver() { }

    /// <summary>
    /// A value type info bound to whichever converter the options actually want.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated resolvers passed <c>JsonMetadataServices.StringConverter</c> and friends
    /// straight through, which pins the converter and silently discards a registered one:
    /// an application that added a <c>JsonConverter&lt;DateTime&gt;</c> to
    /// <see cref="JsonSerializerOptions.Converters"/> got ISO-8601 anyway, because
    /// <c>CreateValueInfo</c> uses the converter it is handed and never consults the options.
    /// <c>AotRequestDeserializer</c> makes that worse by going out of its way to copy converters
    /// from the configured options and from every registered <see cref="JsonSerializerContext"/>
    /// into the options it builds - all of which were then ignored for exactly these types.
    /// </para>
    /// <para>
    /// Nullables did not have the bug and do not need the lookup: <c>GetNullableConverter&lt;T&gt;</c>
    /// resolves the underlying converter through the options already, so a custom
    /// <c>JsonConverter&lt;DateTime&gt;</c> reaches <c>DateTime?</c> on its own.
    /// </para>
    /// <para>
    /// <see cref="JsonConverterFactory"/> is handled because that is what an unbounded generic
    /// converter registers as, and a factory that claims the type is as much a deliberate
    /// registration as a closed converter is.
    /// </para>
    /// </remarks>
    private static JsonTypeInfo<T> Value<T>(JsonSerializerOptions options, JsonConverter<T> builtIn) =>
        JsonMetadataServices.CreateValueInfo<T>(options, Converter(options, builtIn));

    private static JsonConverter<T> Converter<T>(JsonSerializerOptions options, JsonConverter<T> builtIn) {
        // Registration order, first match wins - the same rule System.Text.Json applies itself.
        foreach (var converter in options.Converters) {
            if (converter is JsonConverter<T> direct) {
                return direct;
            }

            if (converter is JsonConverterFactory factory &&
                factory.CanConvert(typeof(T)) &&
                factory.CreateConverter(typeof(T), options) is JsonConverter<T> created) {
                return created;
            }
        }

        return builtIn;
    }

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) {
        // Ordered by how often a description actually produces the type, because the chain is walked
        // linearly - though only once per type per options instance, since JsonSerializerOptions
        // caches what the chain returns. This is a cold-start cost, not a per-request one.
        if (type == typeof(string)) return Value(options, JsonMetadataServices.StringConverter);
        if (type == typeof(bool)) return Value(options, JsonMetadataServices.BooleanConverter);
        if (type == typeof(int)) return Value(options, JsonMetadataServices.Int32Converter);
        if (type == typeof(long)) return Value(options, JsonMetadataServices.Int64Converter);
        if (type == typeof(double)) return Value(options, JsonMetadataServices.DoubleConverter);
        if (type == typeof(DateTimeOffset)) return Value(options, JsonMetadataServices.DateTimeOffsetConverter);
        if (type == typeof(JsonElement)) return Value(options, JsonMetadataServices.JsonElementConverter);
        if (type == typeof(DateOnly)) return Value(options, JsonMetadataServices.DateOnlyConverter);
        if (type == typeof(float)) return Value(options, JsonMetadataServices.SingleConverter);
        if (type == typeof(uint)) return Value(options, JsonMetadataServices.UInt32Converter);
        if (type == typeof(byte[])) return Value(options, JsonMetadataServices.ByteArrayConverter);
        if (type == typeof(decimal)) return Value(options, JsonMetadataServices.DecimalConverter);
        if (type == typeof(Guid)) return Value(options, JsonMetadataServices.GuidConverter);
        if (type == typeof(DateTime)) return Value(options, JsonMetadataServices.DateTimeConverter);
        if (type == typeof(TimeSpan)) return Value(options, JsonMetadataServices.TimeSpanConverter);
        if (type == typeof(TimeOnly)) return Value(options, JsonMetadataServices.TimeOnlyConverter);
        if (type == typeof(byte)) return Value(options, JsonMetadataServices.ByteConverter);
        if (type == typeof(sbyte)) return Value(options, JsonMetadataServices.SByteConverter);
        if (type == typeof(short)) return Value(options, JsonMetadataServices.Int16Converter);
        if (type == typeof(ushort)) return Value(options, JsonMetadataServices.UInt16Converter);
        if (type == typeof(ulong)) return Value(options, JsonMetadataServices.UInt64Converter);
        if (type == typeof(char)) return Value(options, JsonMetadataServices.CharConverter);
        if (type == typeof(Uri)) return Value(options, JsonMetadataServices.UriConverter);
        if (type == typeof(Version)) return Value(options, JsonMetadataServices.VersionConverter);

        // Every value type above, as T?. An optional property of one is what the generated property
        // info asks for by name, so a missing entry here is not a degraded answer - it is null, and
        // the payload cannot be read at all.
        if (type == typeof(bool?)) return Nullable<bool>(options);
        if (type == typeof(int?)) return Nullable<int>(options);
        if (type == typeof(long?)) return Nullable<long>(options);
        if (type == typeof(double?)) return Nullable<double>(options);
        if (type == typeof(DateTimeOffset?)) return Nullable<DateTimeOffset>(options);
        if (type == typeof(JsonElement?)) return Nullable<JsonElement>(options);
        if (type == typeof(DateOnly?)) return Nullable<DateOnly>(options);
        if (type == typeof(float?)) return Nullable<float>(options);
        if (type == typeof(uint?)) return Nullable<uint>(options);
        if (type == typeof(decimal?)) return Nullable<decimal>(options);
        if (type == typeof(Guid?)) return Nullable<Guid>(options);
        if (type == typeof(DateTime?)) return Nullable<DateTime>(options);
        if (type == typeof(TimeSpan?)) return Nullable<TimeSpan>(options);
        if (type == typeof(TimeOnly?)) return Nullable<TimeOnly>(options);
        if (type == typeof(byte?)) return Nullable<byte>(options);
        if (type == typeof(sbyte?)) return Nullable<sbyte>(options);
        if (type == typeof(short?)) return Nullable<short>(options);
        if (type == typeof(ushort?)) return Nullable<ushort>(options);
        if (type == typeof(ulong?)) return Nullable<ulong>(options);
        if (type == typeof(char?)) return Nullable<char>(options);

        return null;
    }

    private static JsonTypeInfo<T?> Nullable<T>(JsonSerializerOptions options) where T : struct =>
        JsonMetadataServices.CreateValueInfo<T?>(
            options, JsonMetadataServices.GetNullableConverter<T>(options));
}
