using System.Text.Json;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Validation;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Errors;

/// <summary>
/// This converter decides both the status code a caller sees and how much of the
/// exception is echoed back to them, so its mapping is worth pinning precisely.
/// </summary>
public class ExceptionToModelConverterTests {

    private static readonly ExceptionToModelConverter Converter = new();

    /// <summary>
    /// Carries a real header dictionary, because an exception that names its own status may also
    /// name headers to send with it.
    /// </summary>
    /// <param name="validationErrorStatus">
    /// The status the operation's contract declared for validation failures, or null for an
    /// operation that declared none.
    /// </param>
    private static IExecutionContext Context(int? validationErrorStatus = null) {
        var response = Substitute.For<IExecutionResponse>();
        response.Headers.Returns(new Dictionary<string, StringValues>());

        var context = Substitute.For<IExecutionContext>();
        context.Response.Returns(response);

        if (validationErrorStatus != null) {
            // The type the pipeline carries, rather than a substitute: ValidationErrorStatus is a
            // default interface member, and a stub for it would prove the stub works.
            context.HandlerInfo.Returns(new ExecutionRequestHandlerInfo(
                "/events", "POST", typeof(ExceptionToModelConverterTests), "Handle",
                validationErrorStatus: validationErrorStatus));
        }

        return context;
    }

    [Fact]
    public void ValidationExceptionMapsTo400WithFieldErrors() {
        var result = ValidationModules.ValidationResult.FromErrors(new[] {
            new ValidationModules.ValidationError("email", "Required", "email is required"),
            new ValidationModules.ValidationError("age", "Range", "age must be between 0 and 120"),
        });

        var (status, model) = Converter.ConvertExceptionToModel(Context(), new ValidationException(result));

        Assert.Equal(400, status);

        var validationError = Assert.IsType<RequestValidationError>(model);
        Assert.Equal("ValidationError", validationError.Type);
        Assert.Equal(2, validationError.Errors.Count);

        Assert.Equal("email", validationError.Errors[0].Field);
        Assert.Equal("Required", validationError.Errors[0].Code);
        Assert.Equal("email is required", validationError.Errors[0].Message);

        Assert.Equal("age", validationError.Errors[1].Field);
        Assert.Equal("Range", validationError.Errors[1].Code);
    }

    [Fact]
    public void ValidationExceptionWithNoErrorsStillMapsTo400() {
        var (status, model) = Converter.ConvertExceptionToModel(
            Context(), new ValidationException(ValidationModules.ValidationResult.Valid));

        Assert.Equal(400, status);
        Assert.Empty(Assert.IsType<RequestValidationError>(model).Errors);
    }

    /// <summary>
    /// A handler that validates by hand throws ValidationModules' exception rather than Hardened's,
    /// and has to reach the same response - one mapper, not two shapes agreeing by duplication.
    /// </summary>
    [Fact]
    public void ValidationModulesValidationExceptionMapsTo400WithFieldErrors() {
        var result = ValidationModules.ValidationResult.FromErrors(new[] {
            new ValidationModules.ValidationError("sku", "pattern", "sku is malformed"),
        });

        var (status, model) = Converter.ConvertExceptionToModel(
            Context(), new ValidationModules.ValidationException(result));

        Assert.Equal(400, status);

        var validationError = Assert.IsType<RequestValidationError>(model);
        Assert.Equal("ValidationError", validationError.Type);

        var fieldError = Assert.Single(validationError.Errors);
        Assert.Equal("sku", fieldError.Field);
        Assert.Equal("pattern", fieldError.Code);
    }

    [Fact]
    public void FormatExceptionMapsTo400() {
        var (status, model) = Converter.ConvertExceptionToModel(
            Context(), new FormatException("not a number"));

        Assert.Equal(400, status);
        var error = Assert.IsType<ErrorModel>(model);
        Assert.Equal(nameof(FormatException), error.Type);
        Assert.Equal("not a number", error.Message);
    }

    [Fact]
    public void BadRequestExceptionMapsTo400() {
        var (status, _) = Converter.ConvertExceptionToModel(
            Context(), new BadRequestException("malformed"));

        Assert.Equal(400, status);
    }

    [Fact]
    public void UnrecognisedExceptionMapsTo500() {
        var (status, model) = Converter.ConvertExceptionToModel(
            Context(), new InvalidOperationException("something went wrong"));

        Assert.Equal(500, status);
        var error = Assert.IsType<ErrorModel>(model);
        Assert.Equal("ServerError", error.Type);
        Assert.DoesNotContain("something went wrong", error.Message);
    }

    private class CustomValidationProblemException : Exception {
        public CustomValidationProblemException() : base("custom") { }
    }

    private class BadgeNotFoundException : Exception {
        public BadgeNotFoundException() : base("no badge") { }
    }

    private class TenantMismatchException : BadRequestException {
        public TenantMismatchException() : base("tenant does not match") { }
    }

    /// <summary>
    /// Classification is by type, not by the shape of the name. A type merely named for
    /// validation, without deriving from BadRequestException, is not a client error.
    /// </summary>
    [Fact]
    public void ExceptionMerelyNamedForValidationIsNotAClientError() {
        var (status, _) = Converter.ConvertExceptionToModel(
            Context(), new CustomValidationProblemException());

        Assert.Equal(500, status);
    }

    /// <summary>
    /// The case that motivated the change: substring matching on "Bad" classified
    /// BadgeNotFoundException - an unrelated type - as a client error.
    /// </summary>
    [Fact]
    public void UnrelatedNameContainingBadIsNotAClientError() {
        var (status, _) = Converter.ConvertExceptionToModel(
            Context(), new BadgeNotFoundException());

        Assert.Equal(500, status);
    }

    /// <summary>
    /// The supported way to have an exception treated as a client error: derive it from
    /// BadRequestException. The name is irrelevant.
    /// </summary>
    [Fact]
    public void DerivingFromBadRequestExceptionMakesItAClientError() {
        var (status, model) = Converter.ConvertExceptionToModel(
            Context(), new TenantMismatchException());

        Assert.Equal(400, status);
        Assert.Equal(nameof(TenantMismatchException), Assert.IsType<ErrorModel>(model).Type);
    }

    /// <summary>
    /// BadContentEncodingException is raised when a client sends a Content-Encoding the request
    /// filter does not decode. It reached 400 first by having "Bad" in its name, then by deriving
    /// from BadRequestException; it is now the 415 RFC 9110 specifies, carrying the codings the
    /// server does accept, which only a status-carrying exception can write.
    /// </summary>
    [Fact]
    public void UnsupportedContentEncodingIsA415NamingWhatIsAccepted() {
        var context = Context();

        var (status, _) = Converter.ConvertExceptionToModel(
            context, new BadContentEncodingException("deflate"));

        Assert.Equal(415, status);
        Assert.Equal("gzip, br", context.Response.Headers["Accept-Encoding"].ToString());
    }

    /// <summary>
    /// Nothing about an unrecognised exception reaches the caller - not its message, not its type.
    /// </summary>
    /// <remarks>
    /// The inverse of this assertion used to stand here, pinning the verbatim echo "so that a
    /// change to message handling is a deliberate decision", and using this same connection string
    /// as its example. The hazard was understood and left open; this is the decision. The exception
    /// still reaches <c>IRequestLogger.RequestFailed</c> in full, which is where an operator reads
    /// it.
    /// </remarks>
    [Fact]
    public void UnrecognisedExceptionTellsTheCallerNothingAboutItself() {
        var (status, model) = Converter.ConvertExceptionToModel(
            Context(), new Exception("connection string 'Server=db;Password=hunter2' failed"));

        var error = Assert.IsType<ErrorModel>(model);

        Assert.Equal(500, status);
        Assert.DoesNotContain("hunter2", error.Message);
        Assert.DoesNotContain("Server=db", error.Message);
        Assert.Equal("ServerError", error.Type);
    }

    /// <summary>
    /// A body the caller sent that could not be read is the caller's error, not the server's.
    /// </summary>
    /// <remarks>
    /// The generated enum and union converters diagnose an undeclared value precisely and raise
    /// <c>JsonException</c> to say so. Every other bad value in the same body - a too-short string,
    /// a number below its minimum - already answered 400 with a field-level list; this one answered
    /// 500 and echoed the exception text, so a client typo read as a server fault.
    /// </remarks>
    [Fact]
    public void UnreadableRequestBodyMapsTo400() {
        var (status, model) = Converter.ConvertExceptionToModel(
            Context(), new JsonException("'cooking' is not a value Genre declares."));

        Assert.Equal(400, status);

        var validationError = Assert.IsType<RequestValidationError>(model);
        var field = Assert.Single(validationError.Errors);

        Assert.Equal("ValidationError", validationError.Type);
        Assert.Equal("body", field.Field);
        Assert.Equal("invalid", field.Code);
        Assert.Equal("'cooking' is not a value Genre declares.", field.Message);
    }

    /// <summary>
    /// System.Text.Json names the member it failed on; that becomes the field, as a validator would
    /// have spelled it.
    /// </summary>
    [Fact]
    public void UnreadableRequestBodyNamesTheFieldFromTheJsonPath() {
        var exception = ThrownReading("{\"genre\":5}", "$.genre");

        var (_, model) = Converter.ConvertExceptionToModel(Context(), exception);

        var field = Assert.Single(Assert.IsType<RequestValidationError>(model).Errors);

        Assert.Equal("body.genre", field.Field);
    }

    /// <summary>
    /// The line and byte position System.Text.Json appends belong in the field, not in prose.
    /// </summary>
    [Fact]
    public void UnreadableRequestBodyDropsThePositionSuffixFromTheMessage() {
        var exception = ThrownReading("{\"genre\":5}", "$.genre");

        var (_, model) = Converter.ConvertExceptionToModel(Context(), exception);

        var field = Assert.Single(Assert.IsType<RequestValidationError>(model).Errors);

        Assert.DoesNotContain("LineNumber", field.Message);
        Assert.DoesNotContain("BytePositionInLine", field.Message);
    }

    /// <summary>
    /// A real one from the serializer, so the shape of <c>Path</c> and <c>Message</c> is the
    /// runtime's rather than this test's idea of it.
    /// </summary>
    private static JsonException ThrownReading(string json, string expectedPath) {
        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PayloadWithAString>(json));

        Assert.Equal(expectedPath, exception.Path);

        return exception;
    }

    /// <summary>
    /// A number where a string is declared, which is what raises a <c>JsonException</c> carrying a
    /// member path. The JSON name is spelled out so the path reads as a wire name would.
    /// </summary>
    private class PayloadWithAString {
        [System.Text.Json.Serialization.JsonPropertyName("genre")]
        public string Genre { get; set; } = "";
    }

    #region status and headers

    /// <summary>
    /// An exception naming a status it does not derive <see cref="StatusCodeException"/> for. Some
    /// statuses are not well-formed without a header - a 401 with no <c>WWW-Authenticate</c> tells a
    /// client to authenticate without saying how - and the interface is what lets one say so.
    /// </summary>
    private class ChallengeException : Exception, IStatusCodeException {
        public int StatusCode => 401;

        public void ApplyHeaders(IDictionary<string, StringValues> headers) {
            headers["WWW-Authenticate"] = "Bearer realm=\"pets\"";
        }
    }

    private class RetryLaterException : StatusCodeException {
        public RetryLaterException() : base(429, value: null, message: "slow down") { }

        public override void ApplyHeaders(IDictionary<string, StringValues> headers) {
            headers["Retry-After"] = "30";
        }
    }

    [Fact]
    public void StatusCodeExceptionCarriesItsOwnStatus() {
        var (status, _) = Converter.ConvertExceptionToModel(Context(), new StatusCodeException(404));

        Assert.Equal(404, status);
    }

    [Fact]
    public void StatusCodeExceptionCarriesItsDeclaredBody() {
        var declared = new { Detail = "no such pet" };

        var (status, model) = Converter.ConvertExceptionToModel(
            Context(), new StatusCodeException(404, declared));

        Assert.Equal(404, status);
        Assert.Same(declared, model);
    }

    /// <summary>
    /// Without a declared body the pipeline still answers with its usual error model, so an
    /// undocumented status produces a sensible response rather than an empty one.
    /// </summary>
    [Fact]
    public void StatusCodeExceptionWithoutABodyFallsBackToTheErrorModel() {
        var (status, model) = Converter.ConvertExceptionToModel(
            Context(), new StatusCodeException(409, value: null, message: "already exists"));

        Assert.Equal(409, status);
        Assert.Equal("already exists", Assert.IsType<ErrorModel>(model).Message);
    }

    /// <summary>
    /// Matched on the interface, not on the class. An exception implementing it directly - which is
    /// what an authentication package would ship rather than deriving from a type in another
    /// assembly - has to reach the same place.
    /// </summary>
    [Fact]
    public void AnExceptionImplementingTheInterfaceDirectlyNamesItsStatus() {
        var (status, model) = Converter.ConvertExceptionToModel(Context(), new ChallengeException());

        Assert.Equal(401, status);
        Assert.Equal(nameof(ChallengeException), Assert.IsType<ErrorModel>(model).Type);
    }

    [Fact]
    public void AnExceptionThatNamesAHeaderHasItAppliedToTheResponse() {
        var context = Context();

        Converter.ConvertExceptionToModel(context, new ChallengeException());

        Assert.Equal("Bearer realm=\"pets\"", context.Response.Headers["WWW-Authenticate"]);
    }

    [Fact]
    public void DerivingFromStatusCodeExceptionAlsoGetsHeadersApplied() {
        var context = Context();

        var (status, _) = Converter.ConvertExceptionToModel(context, new RetryLaterException());

        Assert.Equal(429, status);
        Assert.Equal("30", context.Response.Headers["Retry-After"]);
    }

    /// <summary>
    /// Assigned rather than appended. A retried or forked request that produces the same failure
    /// twice must not send the challenge twice.
    /// </summary>
    [Fact]
    public void ApplyingHeadersTwiceDoesNotDuplicateTheValue() {
        var context = Context();

        Converter.ConvertExceptionToModel(context, new ChallengeException());
        Converter.ConvertExceptionToModel(context, new ChallengeException());

        Assert.Equal(
            new StringValues("Bearer realm=\"pets\""),
            context.Response.Headers["WWW-Authenticate"]);
    }

    /// <summary>
    /// The default adds nothing. Most statuses need no header, and one appearing on a plain 404
    /// would be a surprise.
    /// </summary>
    [Fact]
    public void AStatusCodeExceptionThatNamesNoHeaderAddsNone() {
        var context = Context();

        Converter.ConvertExceptionToModel(context, new StatusCodeException(404));

        Assert.Empty(context.Response.Headers);
    }

    /// <summary>
    /// A validation failure is still a 400 and still carries field errors. The status branch is
    /// checked after it, so adding the interface did not reorder the mapping.
    /// </summary>
    [Fact]
    public void ValidationStillWinsOverTheStatusBranch() {
        var result = ValidationModules.ValidationResult.FromErrors(new[] {
            new ValidationModules.ValidationError("email", "Required", "email is required"),
        });

        var (status, model) = Converter.ConvertExceptionToModel(
            Context(), new ValidationException(result));

        Assert.Equal(400, status);
        Assert.IsType<RequestValidationError>(model);
    }

    #endregion

    #region the declared validation status

    /// <summary>
    /// An operation whose contract declares 422 for validation answers 422 from the deserializer,
    /// not only from the filter.
    /// </summary>
    /// <remarks>
    /// Two spec-first trial arms declared 422 and were answered 400 for an undeclared enum value,
    /// because this branch hardcoded the status the validation branch above it looks up. Which
    /// layer caught the value is not something a caller can see, so it cannot be something the
    /// status depends on - and the 400 it answered was absent from the document the operation
    /// published.
    /// </remarks>
    [Fact]
    public void AnUndeclaredEnumValueAnswersTheDeclaredValidationStatus() {
        var (status, model) = Converter.ConvertExceptionToModel(
            Context(validationErrorStatus: 422),
            new JsonException("'cooking' is not a value Genre declares."));

        Assert.Equal(422, status);
        Assert.IsType<RequestValidationError>(model);
    }

    /// <summary>
    /// Malformed JSON is the same refusal from the same layer, so it answers the same status.
    /// </summary>
    [Fact]
    public void MalformedJsonAnswersTheDeclaredValidationStatus() {
        var (status, model) = Converter.ConvertExceptionToModel(
            Context(validationErrorStatus: 422), ThrownReading("{\"genre\":5}", "$.genre"));

        Assert.Equal(422, status);
        Assert.Equal("body.genre", Assert.Single(
            Assert.IsType<RequestValidationError>(model).Errors).Field);
    }

    /// <summary>
    /// So does an omitted required member, which the deserializer refuses on the validator's behalf
    /// and this converter already answers in the validator's own shape.
    /// </summary>
    [Fact]
    public void AMissingRequiredMemberAnswersTheDeclaredValidationStatus() {
        var (status, model) = Converter.ConvertExceptionToModel(
            Context(validationErrorStatus: 422),
            new JsonException(
                "JSON deserialization for type 'CreateEvent' was missing required properties: 'genre'."));

        Assert.Equal(422, status);
        Assert.Equal("required", Assert.Single(
            Assert.IsType<RequestValidationError>(model).Errors).Code);
    }

    /// <summary>
    /// Both routes to a refused body agree, which is the whole point: one operation, one validation
    /// status.
    /// </summary>
    [Fact]
    public void AConstraintFailureAndAnUnreadableBodyAnswerTheSameStatus() {
        var context = Context(validationErrorStatus: 422);

        var (fromFilter, _) = Converter.ConvertExceptionToModel(
            context,
            new ValidationException(ValidationModules.ValidationResult.FromErrors(new[] {
                new ValidationModules.ValidationError("body.name", "required", "name is required"),
            })));

        var (fromDeserializer, _) = Converter.ConvertExceptionToModel(
            context, new JsonException("'cooking' is not a value Genre declares."));

        Assert.Equal(fromFilter, fromDeserializer);
    }

    /// <summary>
    /// An operation that declared nothing still answers the stock 400, which is what every
    /// code-first handler does.
    /// </summary>
    [Fact]
    public void AnUnreadableBodyStillAnswers400WhereNothingDeclaredAStatus() {
        var (status, _) = Converter.ConvertExceptionToModel(
            Context(), new JsonException("'cooking' is not a value Genre declares."));

        Assert.Equal(400, status);
    }

    /// <summary>
    /// The declared status is for validation, not for everything the operation can fail at. A
    /// server fault on an operation declaring 422 is still a 500.
    /// </summary>
    [Fact]
    public void TheDeclaredValidationStatusDoesNotMoveAnUnrelatedFailure() {
        var (status, _) = Converter.ConvertExceptionToModel(
            Context(validationErrorStatus: 422), new InvalidOperationException("disk on fire"));

        Assert.Equal(500, status);
    }

    #endregion

    #region Timeouts

    /// <summary>
    /// A context whose handler carries <paramref name="metadata"/>, which is where the converter
    /// reads a declared deadline's status from.
    /// </summary>
    private static IExecutionContext ContextFor(params object[] metadata) {
        var response = Substitute.For<IExecutionResponse>();
        response.Headers.Returns(new Dictionary<string, StringValues>());

        var context = Substitute.For<IExecutionContext>();
        context.Response.Returns(response);
        context.HandlerInfo.Returns(new ExecutionRequestHandlerInfo(
            "/rates", "GET", typeof(ExceptionToModelConverterTests), "Read", metadata: metadata));

        return context;
    }

    /// <summary>
    /// The status the timeout feature produces. It comes from here rather than from
    /// <c>TimeoutFilter</c> because serialization happens inside the filter's span - by the time
    /// the filter regains control the response is already written. Without this rule the whole
    /// feature answers 500 and is invisible.
    /// </summary>
    [Fact]
    public void ACancelledRequestOnABoundedHandlerIs504() {
        var (status, model) = Converter.ConvertExceptionToModel(
            ContextFor(new TimeoutAttribute()), new OperationCanceledException());

        Assert.Equal(504, status);
        Assert.Equal("GatewayTimeout", Assert.IsType<ErrorModel>(model).Type);
    }

    /// <summary>
    /// A handler nothing bounds has no deadline to have missed, so whatever cancelled it is not
    /// this framework's timeout and is not described as one. ASP.NET Core draws the line in the
    /// same place: its <c>DefaultPolicy</c> is null until an application sets one, and its
    /// middleware answers only for endpoints a policy covers.
    /// </summary>
    [Fact]
    public void ACancelledRequestOnAnUnboundedHandlerIsStillAServerFault() {
        var (status, model) = Converter.ConvertExceptionToModel(
            Context(), new OperationCanceledException());

        Assert.Equal(500, status);
        Assert.Equal("ServerError", Assert.IsType<ErrorModel>(model).Type);
    }

    /// <summary>
    /// What <c>Task.Delay</c> and every <c>HttpClient</c> call actually throw. A rule matching
    /// only <c>OperationCanceledException</c> by exact type would miss every real timeout.
    /// </summary>
    [Fact]
    public void ATaskCancelledOnTheDeadlineIs504() {
        var (status, _) = Converter.ConvertExceptionToModel(
            ContextFor(new TimeoutAttribute()), new TaskCanceledException());

        Assert.Equal(504, status);
    }

    /// <summary>
    /// A client that hangs up cancels the same linked token and arrives as the same exception, so
    /// it reads as 504 too. Nobody receives it either way, and 504 describes a disconnect less
    /// wrongly than 500 does.
    /// </summary>
    [Fact]
    public void ADisconnectOnABoundedHandlerReadsTheSameAsADeadline() {
        using var disconnected = new CancellationTokenSource();
        disconnected.Cancel();

        var (status, _) = Converter.ConvertExceptionToModel(
            ContextFor(new TimeoutAttribute()),
            new OperationCanceledException(disconnected.Token));

        Assert.Equal(504, status);
    }

    /// <summary>
    /// An operation shedding load rather than waiting on something says so, and only that spelling
    /// can: the application-wide default has no handler metadata to be read from and always
    /// answers 504.
    /// </summary>
    [Fact]
    public void ADeclaredStatusIsWhatTheCallerSees() {
        var context = ContextFor(new TimeoutAttribute { Status = 503, RetryAfterSeconds = 30 });

        var (status, model) = Converter.ConvertExceptionToModel(context, new TaskCanceledException());

        Assert.Equal(503, status);
        Assert.Equal("ServiceUnavailable", Assert.IsType<ErrorModel>(model).Type);
        Assert.Equal("30", context.Response.Headers[KnownHeaders.RetryAfter]);
    }

    /// <summary>
    /// A deadline out at a dependency knows nothing about when that dependency recovers, so the
    /// default sends no number.
    /// </summary>
    [Fact]
    public void A504SendsNoRetryAfter() {
        var context = ContextFor(new TimeoutAttribute());

        Converter.ConvertExceptionToModel(context, new TaskCanceledException());

        Assert.False(context.Response.Headers.ContainsKey(KnownHeaders.RetryAfter));
    }

    /// <summary>
    /// Nearest wins, and the metadata order is the precedence: an operation's own declaration is
    /// emitted ahead of its class's, so the method's status is the one a caller sees even where the
    /// class asked for something else.
    /// </summary>
    [Fact]
    public void TheNearestDeclarationIsTheOneThatDecidesTheStatus() {
        var context = ContextFor(
            new TimeoutAttribute { Milliseconds = 2000 },                  // the method
            new TimeoutAttribute { Milliseconds = 500, Status = 503 });    // its class

        var (status, _) = Converter.ConvertExceptionToModel(context, new TaskCanceledException());

        Assert.Equal(504, status);
    }

    /// <summary>
    /// Nothing about the timeout reaches the caller beyond the fact of it, which is the same rule
    /// the anonymous 500 follows.
    /// </summary>
    [Fact]
    public void TheTimeoutBodyCarriesNothingAboutTheRequest() {
        var (_, model) = Converter.ConvertExceptionToModel(
            ContextFor(new TimeoutAttribute()),
            new OperationCanceledException("upstream rates.example.com never answered"));

        Assert.DoesNotContain("rates.example.com", Assert.IsType<ErrorModel>(model).Message);
    }

    #endregion
}
