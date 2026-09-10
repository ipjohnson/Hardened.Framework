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
/// What a caller is told about a body the reader could not use, and whose name is on it.
/// </summary>
/// <remarks>
/// <para>
/// The trial's B-08: <c>{"accountId":</c> was answered <c>body.accountId is invalid</c>, and
/// <c>accountId</c> is not at fault - the caller's JSON is. A truncated document has a path like
/// any other failure, and it means something different: wherever the reader had reached when the
/// text ran out, which is whichever member happens to be last.
/// </para>
/// <para>
/// Real deserialization failures throughout, never a hand-written <c>JsonException</c>. A
/// fabricated one carries no inner exception, which is the signal being read.
/// </para>
/// </remarks>
public class MalformedBodyTests {

    private static readonly ExceptionToModelConverter Converter = new();

    private record Quote(
        [property: JsonPropertyName("accountId")] string AccountId,
        [property: JsonPropertyName("weightKg")] int WeightKg);

    private static IExecutionContext Context() {
        var response = Substitute.For<IExecutionResponse>();
        response.Headers.Returns(new Dictionary<string, StringValues>());

        var context = Substitute.For<IExecutionContext>();
        context.Response.Returns(response);

        return context;
    }

    private static RequestValidationFieldError Refused(string json) {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Quote>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var (status, model) = Converter.ConvertExceptionToModel(Context(), exception);

        Assert.Equal(400, status);

        return Assert.Single(Assert.IsType<RequestValidationError>(model).Errors!);
    }

    /// <summary>
    /// A document that does not parse is the body's fault, whatever the reader's path says. The
    /// path here is <c>$.accountId</c>, which is where the text ran out.
    /// </summary>
    [Theory]
    [InlineData("""{"accountId":""")]
    [InlineData("""{"accountId":"A""")]
    [InlineData("""{"accountId":"A","weightKg":1""")]
    [InlineData("x")]
    public void AMalformedDocumentIsReportedAgainstTheBody(string json) {
        var error = Refused(json);

        Assert.Equal("body", error.Field);
        Assert.Equal("invalid", error.Code);
    }

    /// <summary>
    /// A value the reader understood and could not convert keeps its own name: that path is the
    /// field at fault, and naming it is the whole use of the answer.
    /// </summary>
    [Fact]
    public void AValueOfTheWrongTypeIsReportedAgainstItsOwnMember() {
        var error = Refused("""{"accountId":"A","weightKg":"heavy"}""");

        Assert.Equal("body.weightKg", error.Field);
        Assert.Equal("invalid", error.Code);
    }

    /// <summary>
    /// An empty payload is not malformed JSON, it is the absence of one - the same thing a literal
    /// <c>null</c> body is, which the binder refuses with this error's twin. A caller was being
    /// handed the reader's diagnostics instead: <c>"The input does not contain any JSON tokens.
    /// Expected the input to start with a valid JSON token, when isFinalBlock is true."</c>
    /// </summary>
    [Fact]
    public void AnEmptyBodyIsReportedAsMissingRatherThanMalformed() {
        var error = Refused("");

        Assert.Equal("body", error.Field);
        Assert.Equal("required", error.Code);
        Assert.Equal("body is required.", error.Message);
    }

    /// <summary>The prefix is the handler's own body parameter identifier wherever it appears.</summary>
    [Fact]
    public void TheFieldIsTheHandlersBodyParameter() {
        var context = Context();

        context.HandlerInfo.Returns(new Hardened.Requests.Runtime.Execution.ExecutionRequestHandlerInfo(
            "/quotes", "POST", typeof(object), "Create", bodyParameterName: "request"));

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Quote>(
            """{"accountId":""", new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var (_, model) = Converter.ConvertExceptionToModel(context, exception);

        Assert.Equal(
            "request",
            Assert.Single(Assert.IsType<RequestValidationError>(model).Errors!).Field);
    }
}

/// <summary>
/// The binder's half of the same answer.
/// </summary>
/// <remarks>
/// A JSON <c>null</c> deserializes without complaint, so no exception reaches the converter and the
/// refusal has to be made where the value lands. Same field, same code, same sentence an empty body
/// gets - see <c>MalformedBodyTests.AnEmptyBodyIsReportedAsMissingRatherThanMalformed</c>.
/// </remarks>
public class RequestBodyTests {

    [Fact]
    public void ANullBodyIsRefusedAsRequired() {
        var exception = Assert.Throws<ValidationException>(
            () => RequestBody.Required<string>(null, "request"));

        var error = Assert.Single(exception.ValidationResult.Errors);

        Assert.Equal("request", error.Field);
        Assert.Equal("required", error.Code);
        Assert.Equal("request is required.", error.Message);
    }

    [Fact]
    public void ABodyThatArrivedIsHandedBack() {
        Assert.Equal("sent", RequestBody.Required("sent", "request"));
    }
}

/// <summary>
/// A canary over System.Text.Json's own behaviour, in the shape
/// <c>MissingRequiredMembersMessageTests</c> established.
/// </summary>
/// <remarks>
/// Telling a malformed document from a value that would not convert is read off the inner
/// exception's type name, because there is no public type for it and the prose is worse to depend
/// on. An SDK that stops wrapping reader failures that way fails here, naming the reason, rather
/// than quietly putting a member's name on the caller's syntax error again.
/// </remarks>
public class MalformedBodyMessageTests {

    private record Quote(
        [property: JsonPropertyName("accountId")] string AccountId,
        [property: JsonPropertyName("weightKg")] int WeightKg);

    private static JsonException Deserialize(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Quote>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    [Theory]
    [InlineData("""{"accountId":""")]
    [InlineData("x")]
    [InlineData("")]
    public void AReaderFailureIsStillWrappedInAJsonReaderException(string json) {
        Assert.Equal("JsonReaderException", Deserialize(json).InnerException?.GetType().Name);
    }

    /// <summary>A conversion failure is not, which is what makes the type a signal.</summary>
    [Fact]
    public void AConversionFailureIsStillNotAReaderException() {
        Assert.NotEqual(
            "JsonReaderException",
            Deserialize("""{"accountId":"A","weightKg":"heavy"}""").InnerException?.GetType().Name);
    }

    /// <summary>The sentence an empty payload is still told apart by.</summary>
    [Fact]
    public void AnEmptyPayloadStillSaysItCarriesNoTokens() {
        Assert.StartsWith("The input does not contain any JSON tokens", Deserialize("").Message);
    }
}
