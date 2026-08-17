using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Errors;
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
    private static IExecutionContext Context() {
        var response = Substitute.For<IExecutionResponse>();
        response.Headers.Returns(new Dictionary<string, StringValues>());

        var context = Substitute.For<IExecutionContext>();
        context.Response.Returns(response);

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
        Assert.Equal(nameof(InvalidOperationException), error.Type);
        Assert.Equal("something went wrong", error.Message);
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
    /// BadContentEncodingException is raised when a client sends an unsupported
    /// Content-Encoding, so it is a client error. It previously reached 400 only because
    /// its name contains "Bad"; it now derives from BadRequestException and earns it.
    /// </summary>
    [Fact]
    public void UnsupportedContentEncodingIsAClientError() {
        var (status, _) = Converter.ConvertExceptionToModel(
            Context(), new BadContentEncodingException("deflate"));

        Assert.Equal(400, status);
    }

    /// <summary>
    /// The exception message is echoed to the caller verbatim for unrecognised exceptions.
    /// Worth pinning so that a change to message handling is a deliberate decision.
    /// </summary>
    [Fact]
    public void UnrecognisedExceptionMessageIsEchoedVerbatim() {
        var (_, model) = Converter.ConvertExceptionToModel(
            Context(), new Exception("connection string 'Server=db;Password=hunter2' failed"));

        Assert.Equal("connection string 'Server=db;Password=hunter2' failed",
            Assert.IsType<ErrorModel>(model).Message);
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
}
