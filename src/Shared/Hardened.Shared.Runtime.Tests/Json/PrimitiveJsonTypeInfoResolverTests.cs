using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hardened.Shared.Runtime.Json;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Json;

public class PrimitiveJsonTypeInfoResolverTests {

    private static JsonSerializerOptions Options(params JsonConverter[] converters) {
        var options = new JsonSerializerOptions();

        foreach (var converter in converters) {
            options.Converters.Add(converter);
        }

        options.TypeInfoResolverChain.Add(PrimitiveJsonTypeInfoResolver.Instance);

        return options;
    }

    public static TheoryData<Type> LeafTypes() => new() {
        typeof(string), typeof(bool), typeof(int), typeof(long), typeof(double),
        typeof(DateTimeOffset), typeof(JsonElement), typeof(DateOnly), typeof(float),
        typeof(uint), typeof(byte[]), typeof(decimal), typeof(Guid), typeof(DateTime),
        typeof(TimeSpan), typeof(TimeOnly), typeof(byte), typeof(sbyte), typeof(short),
        typeof(ushort), typeof(ulong), typeof(char), typeof(Uri), typeof(Version),
        typeof(bool?), typeof(int?), typeof(long?), typeof(double?), typeof(DateTimeOffset?),
        typeof(JsonElement?), typeof(DateOnly?), typeof(float?), typeof(uint?), typeof(decimal?),
        typeof(Guid?), typeof(DateTime?), typeof(TimeSpan?), typeof(TimeOnly?), typeof(byte?),
        typeof(sbyte?), typeof(short?), typeof(ushort?), typeof(ulong?), typeof(char?),
    };

    [Theory]
    [MemberData(nameof(LeafTypes))]
    public void GetTypeInfo_AnswersEveryLeafType(Type type) {
        var info = PrimitiveJsonTypeInfoResolver.Instance.GetTypeInfo(type, Options());

        Assert.NotNull(info);
        Assert.Equal(type, info.Type);
    }

    [Fact]
    public void GetTypeInfo_ReturnsNullForATypeItDoesNotOwn() {
        Assert.Null(PrimitiveJsonTypeInfoResolver.Instance.GetTypeInfo(typeof(Uri[]), Options()));
    }

    /// <summary>
    /// <c>object</c> is deliberately not answered: a type info for it would shadow the polymorphic
    /// handling a generated resolver sets up on the base of a hierarchy.
    /// </summary>
    [Fact]
    public void GetTypeInfo_DoesNotAnswerForObject() {
        Assert.Null(PrimitiveJsonTypeInfoResolver.Instance.GetTypeInfo(typeof(object), Options()));
    }

    /// <summary>
    /// The reason a property info can be created at all - it resolves its converter by asking the
    /// options, which walks the chain, so a chain that cannot answer <c>typeof(string)</c> throws on
    /// every string property rather than degrading.
    /// </summary>
    [Fact]
    public void GetTypeInfo_SuppliesTheLeafMetadataAPropertyInfoResolves() {
        var options = Options();

        var info = JsonMetadataServices.CreateObjectInfo<Leaf>(options, new JsonObjectInfoValues<Leaf> {
            ObjectWithParameterizedConstructorCreator = static args => new Leaf((string)args[0], (int?)args[1]),
            PropertyMetadataInitializer = _ => new JsonPropertyInfo[] {
                JsonMetadataServices.CreatePropertyInfo<string>(options, new JsonPropertyInfoValues<string> {
                    IsProperty = true, IsPublic = true, DeclaringType = typeof(Leaf),
                    PropertyName = "name", Getter = static o => ((Leaf)o).Name, Setter = null,
                }),
                JsonMetadataServices.CreatePropertyInfo<int?>(options, new JsonPropertyInfoValues<int?> {
                    IsProperty = true, IsPublic = true, DeclaringType = typeof(Leaf),
                    PropertyName = "count", Getter = static o => ((Leaf)o).Count, Setter = null,
                }),
            },
            ConstructorParameterMetadataInitializer = static () => new JsonParameterInfoValues[] {
                new() { Name = "name", ParameterType = typeof(string), Position = 0 },
                new() { Name = "count", ParameterType = typeof(int?), Position = 1, DefaultValue = null },
            },
        });

        Assert.Equal("""{"name":"a","count":2}""", JsonSerializer.Serialize(new Leaf("a", 2), info));
        Assert.Equal(new Leaf("a", 2), JsonSerializer.Deserialize("""{"name":"a","count":2}""", info));
    }

    [Fact]
    public void GetTypeInfo_PrefersARegisteredConverterOverTheBuiltInOne() {
        var options = Options(new UnixSecondsDateTimeConverter());

        var info = (JsonTypeInfo<DateTime>)options.GetTypeInfo(typeof(DateTime));

        Assert.IsType<UnixSecondsDateTimeConverter>(info.Converter);
        Assert.Equal("1755388800", JsonSerializer.Serialize(Timestamp, info));
    }

    [Fact]
    public void GetTypeInfo_FallsBackToTheBuiltInConverterWhenNoneIsRegistered() {
        var info = (JsonTypeInfo<DateTime>)Options().GetTypeInfo(typeof(DateTime));

        Assert.Equal("\"2025-08-17T00:00:00Z\"", JsonSerializer.Serialize(Timestamp, info));
    }

    /// <summary>
    /// <c>GetNullableConverter&lt;T&gt;</c> resolves the underlying converter through the options, so
    /// the nullable form picks up a registered converter without the lookup the non-nullable form
    /// needs.
    /// </summary>
    [Fact]
    public void GetTypeInfo_RoutesTheNullableFormThroughARegisteredConverter() {
        var options = Options(new UnixSecondsDateTimeConverter());

        var info = (JsonTypeInfo<DateTime?>)options.GetTypeInfo(typeof(DateTime?));

        Assert.Equal("1755388800", JsonSerializer.Serialize((DateTime?)Timestamp, info));
    }

    [Fact]
    public void GetTypeInfo_PrefersAConverterAFactoryProduces() {
        var options = Options(new ShoutingStringConverterFactory());

        var info = (JsonTypeInfo<string>)options.GetTypeInfo(typeof(string));

        Assert.Equal("\"ABC\"", JsonSerializer.Serialize("abc", info));
    }

    private static readonly DateTime Timestamp = new(2025, 8, 17, 0, 0, 0, DateTimeKind.Utc);

    private record Leaf(string Name, int? Count);

    private sealed class UnixSecondsDateTimeConverter : JsonConverter<DateTime> {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64()).UtcDateTime;

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(new DateTimeOffset(value, TimeSpan.Zero).ToUnixTimeSeconds());
    }

    private sealed class ShoutingStringConverterFactory : JsonConverterFactory {
        public override bool CanConvert(Type type) => type == typeof(string);

        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
            new ShoutingStringConverter();

        private sealed class ShoutingStringConverter : JsonConverter<string> {
            public override string Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
                reader.GetString() ?? "";

            public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value.ToUpperInvariant());
        }
    }
}
