using System.Text.Json;
using System.Text.Json.Serialization;
using Hardened.Shared.Runtime.Json;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Json;

/// <summary>
/// The shared serializer names enums, without outranking one that knows its own wire form.
/// </summary>
/// <remarks>
/// <para>
/// A bare <c>JsonStringEnumConverter</c> sat in <c>Options.Converters</c>. System.Text.Json ranks a
/// converter in that collection above a <c>[JsonConverter]</c> attribute on the type - documented,
/// and reproduced by <see cref="AConverterInTheCollectionOutranksATypeAttribute"/> below - so the
/// generated converter never ran through this serializer and it wrote the C# member name.
/// </para>
/// <para>
/// It bit hardest in the test host, whose <c>Post(object, path)</c> serializes with exactly this
/// service while the request deserializer honours the attribute: the harness wrote a value its own
/// application then refused, and every test posting a record with an enum in it got a 500.
/// </para>
/// </remarks>
public class DeclaredEnumsFirstConverterTests {

    [JsonConverter(typeof(GenreConverter))]
    private enum Genre { ScienceFiction, Fiction }

    private enum Plain { Draft, Published }

    private class GenreConverter : JsonConverter<Genre> {
        public override Genre Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.GetString() switch {
                "science-fiction" => Genre.ScienceFiction,
                "fiction" => Genre.Fiction,
                var other => throw new JsonException($"'{other}' is not a value Genre declares.")
            };

        public override void Write(Utf8JsonWriter writer, Genre value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value == Genre.ScienceFiction ? "science-fiction" : "fiction");
    }

    private static JsonSerializerOptions Shared() => new JsonSerializerConfiguration().Options;

    /// <summary>
    /// The System.Text.Json behaviour the defect rests on, pinned so the fix is not mistaken for
    /// superstition. A converter in the collection wins over the attribute on the type.
    /// </summary>
    [Fact]
    public void AConverterInTheCollectionOutranksATypeAttribute() {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        Assert.Equal("\"ScienceFiction\"", JsonSerializer.Serialize(Genre.ScienceFiction, options));
    }

    [Fact]
    public void AnEnumWithItsOwnConverterKeepsIt() {
        Assert.Equal(
            "\"science-fiction\"", JsonSerializer.Serialize(Genre.ScienceFiction, Shared()));
    }

    [Fact]
    public void AnEnumWithItsOwnConverterReadsBack() {
        Assert.Equal(
            Genre.ScienceFiction,
            JsonSerializer.Deserialize<Genre>("\"science-fiction\"", Shared()));
    }

    /// <summary>
    /// A round trip through the shared options alone, which is what the harness does and what threw.
    /// </summary>
    [Fact]
    public void AnEnumWithItsOwnConverterRoundTrips() {
        var options = Shared();

        var json = JsonSerializer.Serialize(Genre.ScienceFiction, options);

        Assert.Equal(Genre.ScienceFiction, JsonSerializer.Deserialize<Genre>(json, options));
    }

    /// <summary>
    /// An application's own enum still serializes as a name rather than a number.
    /// </summary>
    /// <remarks>
    /// Why the converter declines by <c>CanConvert</c> rather than being dropped outright. Nothing
    /// generated a converter for this one, and writing <c>1</c> for it would be a different
    /// regression in the same place.
    /// </remarks>
    [Fact]
    public void AnEnumWithNoConverterOfItsOwnIsStillNamed() {
        Assert.Equal("\"Published\"", JsonSerializer.Serialize(Plain.Published, Shared()));
    }

    [Fact]
    public void AnEnumWithNoConverterOfItsOwnReadsItsNameBack() {
        Assert.Equal(Plain.Draft, JsonSerializer.Deserialize<Plain>("\"Draft\"", Shared()));
    }

    /// <summary>Nullable forms follow the type they wrap, in both directions.</summary>
    [Fact]
    public void ANullableEnumFollowsItsUnderlyingType() {
        Assert.Equal(
            "\"science-fiction\"", JsonSerializer.Serialize((Genre?)Genre.ScienceFiction, Shared()));
        Assert.Equal("\"Published\"", JsonSerializer.Serialize((Plain?)Plain.Published, Shared()));
    }
}
