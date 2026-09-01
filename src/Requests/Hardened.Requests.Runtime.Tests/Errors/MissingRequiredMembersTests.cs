using System.Text.Json;
using System.Text.Json.Serialization;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Errors;

/// <summary>
/// What a caller is told when they omit a required member the C# type cannot prove was sent.
/// </summary>
/// <remarks>
/// <para>
/// A required member of a value type carries no <c>[Required]</c> - the validation generator emits
/// <c>value.x is null</c>, which is CS0037 against an <c>int</c> - so absence used to become
/// <c>default(T)</c> in silence. An omitted enum became its first declared member and the API
/// answered <b>201</b> with a category the caller never sent.
/// </para>
/// <para>
/// The deserializer catches it now, and these pin the answer it produces being the same shape the
/// validator produces for a reference type. Which layer caught it is the framework's business.
/// </para>
/// </remarks>
public class MissingRequiredMembersTests {

    private static readonly ExceptionToModelConverter Converter = new();

    private static IExecutionContext Context() {
        var response = Substitute.For<IExecutionResponse>();
        response.Headers.Returns(new Dictionary<string, StringValues>());

        var context = Substitute.For<IExecutionContext>();
        context.Response.Returns(response);

        return context;
    }

    private static RequestValidationError Convert(
        JsonException exception, IExecutionContext? context = null) {
        var (status, model) = Converter.ConvertExceptionToModel(context ?? Context(), exception);

        Assert.Equal(400, status);

        return Assert.IsType<RequestValidationError>(model);
    }

    /// <summary>
    /// A real deserialization failure, not a hand-written message. The fabricated form omitted
    /// the quoting and the closing period the real message carries, which is how a parser that
    /// produced `body.'code'.` passed every test here while malforming every field in
    /// production.
    /// </summary>
    private static JsonException Missing(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Product>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private record Product(
        [property: JsonPropertyName("sku")] string Sku,
        [property: JsonPropertyName("category")] [property: JsonRequired] int Category,
        [property: JsonPropertyName("unitPriceCents")] [property: JsonRequired] int UnitPriceCents);

    /// <summary>
    /// One missing member, reported as the validator would report it: same field spelling, same
    /// code, same wording - and no trace of the message's own quoting or punctuation.
    /// </summary>
    [Fact]
    public void AMissingMemberIsReportedAsARequiredFieldError() {
        var error = Assert.Single(
            Convert(Missing("""{"sku":"A","category":1}""")).Errors!);

        Assert.Equal("body.unitPriceCents", error.Field);
        Assert.Equal("required", error.Code);
        Assert.Equal("body.unitPriceCents is required.", error.Message);
    }

    /// <summary>
    /// Every missing member, not the first. System.Text.Json aggregates, so the caller can fix
    /// their request in one pass rather than one round trip per field.
    /// </summary>
    [Fact]
    public void EveryMissingMemberIsReported() {
        var errors = Convert(Missing("""{"sku":"A"}""")).Errors!;

        Assert.Equal(
            new[] { "body.category", "body.unitPriceCents" },
            errors.Select(error => error.Field));

        Assert.All(errors, error => Assert.Equal("required", error.Code));
    }

    /// <summary>
    /// The prefix is the handler's own body parameter identifier, which is what the generated
    /// validators use - "body" was hardcoded, so the filter and the deserializer disagreed about
    /// the same member's path on any handler that named its parameter something else.
    /// </summary>
    [Fact]
    public void TheFieldPrefixIsTheHandlersBodyParameter() {
        var context = Context();

        context.HandlerInfo.Returns(new Hardened.Requests.Runtime.Execution.ExecutionRequestHandlerInfo(
            "/products", "POST", typeof(object), "Create", bodyParameterName: "request"));

        var errors = Convert(Missing("""{"sku":"A"}"""), context).Errors!;

        Assert.Equal(
            new[] { "request.category", "request.unitPriceCents" },
            errors.Select(error => error.Field));
    }

    /// <summary>
    /// Any other <c>JsonException</c> keeps the general body-read answer. Malformed JSON is not a
    /// missing member, and claiming otherwise would name fields the caller never wrote.
    /// </summary>
    [Theory]
    [InlineData("The JSON value could not be converted to Category. Path: $.category | LineNumber: 0 | BytePositionInLine: 29.")]
    [InlineData("'x' is an invalid start of a value.")]
    [InlineData("JSON deserialization for type 'Product' failed for some other reason")]
    public void AnyOtherFailureKeepsTheGeneralAnswer(string message) {
        var error = Assert.Single(Convert(new JsonException(message)).Errors!);

        Assert.Equal("invalid", error.Code);
    }
}

/// <summary>
/// A canary over System.Text.Json's own behaviour.
/// </summary>
/// <remarks>
/// <para>
/// <c>ExceptionToModelConverter</c> reads the missing-member list out of the exception's message,
/// because there is no typed exception for it and <c>JsonException</c> carries no structured member
/// list - its <c>Path</c> is <c>$</c>, since the object rather than any one member is what failed.
/// </para>
/// <para>
/// That is a dependency on wording, and this is the test that makes it safe: an SDK that changes the
/// message fails here, naming the reason, rather than silently degrading every missing-member 400 to
/// the general body-read answer in production.
/// </para>
/// </remarks>
public class MissingRequiredMembersMessageTests {

    private record Product(
        [property: JsonPropertyName("sku")] string Sku,
        [property: JsonPropertyName("category")] [property: JsonRequired] int Category,
        [property: JsonPropertyName("unitPriceCents")] [property: JsonRequired] int UnitPriceCents,
        [property: JsonPropertyName("stock")] int? Stock = default);

    private static JsonException Deserialize(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Product>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    /// <summary>
    /// The prefix and marker the converter matches on, and the ": " the member list follows.
    /// </summary>
    [Fact]
    public void TheMessageStillCarriesThePrefixTheConverterMatches() {
        var message = Deserialize("""{"sku":"A"}""").Message;

        Assert.StartsWith("JSON deserialization for type ", message);
        Assert.Contains("missing required properties", message);
        Assert.Contains(": ", message);
    }

    /// <summary>
    /// Still aggregated, and still by wire name rather than by C# member name.
    /// </summary>
    [Fact]
    public void EveryMissingMemberIsStillNamedByItsWireName() {
        var message = Deserialize("""{"sku":"A"}""").Message;

        Assert.Contains("category", message);
        Assert.Contains("unitPriceCents", message);
        Assert.DoesNotContain("UnitPriceCents", message);
    }

    /// <summary>
    /// Still the object, not the member. This is why the member list cannot be read off
    /// <c>Path</c>.
    /// </summary>
    [Fact]
    public void ThePathIsStillTheObject() {
        Assert.Equal("$", Deserialize("""{"sku":"A"}""").Path);
    }
}
