using System.Text.Json;
using System.Text.Json.Serialization;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Errors;

/// <summary>
/// What a caller is told about a body value that could not be read: the kind of value that goes
/// there, and a date-time that states no offset.
/// </summary>
public class BodyValueErrorTests
{
    private static readonly ExceptionToModelConverter Converter = new();

    // Classes with setters rather than records: a value bound through a constructor parameter is
    // reported against the record's own type, so the message could not name the member's.
    private sealed class Quote
    {
        [JsonPropertyName("weightKg")]
        public int WeightKg { get; set; }
    }

    private sealed class Window
    {
        [JsonPropertyName("endsAt")]
        public DateTimeOffset EndsAt { get; set; }
    }

    private static IExecutionContext Context()
    {
        var response = Substitute.For<IExecutionResponse>();
        response.Headers.Returns(new Dictionary<string, StringValues>());

        var context = Substitute.For<IExecutionContext>();
        context.Response.Returns(response);

        return context;
    }

    /// <summary>The options the request deserializer reads with, converter included.</summary>
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        options.Converters.Add(OffsetRequiredDateTimeOffsetConverter.Instance);

        return options;
    }

    private static RequestValidationFieldError Refused<T>(string json)
    {
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<T>(json, Options())
        );

        var (status, model) = Converter.ConvertExceptionToModel(Context(), exception);

        Assert.Equal(400, status);

        return Assert.Single(Assert.IsType<RequestValidationError>(model).Errors!);
    }

    /// <summary>
    /// The member is named, and the message says what goes there. It used to name
    /// <c>System.Int32</c>.
    /// </summary>
    [Fact]
    public void AValueOfTheWrongTypeSaysWhatKindOfValueGoesThere()
    {
        var error = Refused<Quote>("""{"weightKg":"heavy"}""");

        Assert.Equal("body.weightKg", error.Field);
        Assert.Equal("The value is not an integer this field can hold.", error.Message);
    }

    [Theory]
    [InlineData("System.Boolean", "The value is not true or false.")]
    [InlineData("System.Guid", "The value is not a UUID.")]
    [InlineData("System.Decimal", "The value is not a number this field can hold.")]
    [InlineData("System.DateTimeOffset", "The value is not a date-time.")]
    [InlineData("System.String[]", "The value is not an array.")]
    [InlineData("Todos.NewTodo", "The value is not of the type this field takes.")]
    public void TheConversionMessageNamesTheKindOfValue(string type, string expected)
    {
        var (_, model) = Converter.ConvertExceptionToModel(
            Context(),
            new JsonException(
                "The JSON value could not be converted to "
                    + type
                    + ". Path: $.value | LineNumber: 0 | BytePositionInLine: 9.",
                "$.value",
                0,
                9
            )
        );

        Assert.Equal(
            expected,
            Assert.Single(Assert.IsType<RequestValidationError>(model).Errors!).Message
        );
    }

    /// <summary>A converter's own sentence is kept, such as a generated enum's.</summary>
    [Fact]
    public void AConvertersOwnMessageIsKept()
    {
        var (_, model) = Converter.ConvertExceptionToModel(
            Context(),
            new JsonException("'cooking' is not a value Genre declares.", "$.genre", 0, 9)
        );

        Assert.Equal(
            "'cooking' is not a value Genre declares.",
            Assert.Single(Assert.IsType<RequestValidationError>(model).Errors!).Message
        );
    }

    /// <summary>
    /// A date-time with no offset is refused, where it was read as the server's local time.
    /// </summary>
    [Theory]
    [InlineData("2030-01-01T00:00:00")]
    [InlineData("2030-01-01T00:00:00.123")]
    [InlineData("2030-01-01")]
    public void ADateTimeWithNoOffsetIsRefused(string value)
    {
        var error = Refused<Window>("{\"endsAt\":\"" + value + "\"}");

        Assert.Equal("body.endsAt", error.Field);
        Assert.Equal(
            "The value is not an RFC 3339 date-time: it states no offset, such as Z or -05:00.",
            error.Message
        );
    }

    [Theory]
    [InlineData("2030-01-01T00:00:00Z", 0)]
    [InlineData("2030-01-01T00:00:00+01:00", 60)]
    [InlineData("2030-01-01T00:00:00.5-05:30", -330)]
    public void ADateTimeWithAnOffsetIsReadWithIt(string value, int offsetMinutes)
    {
        var window = JsonSerializer.Deserialize<Window>(
            "{\"endsAt\":\"" + value + "\"}",
            Options()
        )!;

        Assert.Equal(TimeSpan.FromMinutes(offsetMinutes), window.EndsAt.Offset);
    }

    /// <summary>A value that is not a date-time at all gets the serializer's own message.</summary>
    [Fact]
    public void ANumberWhereADateTimeGoesSaysSo()
    {
        var error = Refused<Window>("""{"endsAt":42}""");

        Assert.Equal("The value is not a date-time.", error.Message);
    }

    /// <summary>A response is written as the serializer writes it, offset and all.</summary>
    [Fact]
    public void AResponseIsWrittenUnchanged()
    {
        var value = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.FromHours(1));

        Assert.Equal(
            JsonSerializer.Serialize(new Window { EndsAt = value }),
            JsonSerializer.Serialize(new Window { EndsAt = value }, Options())
        );
    }
}
