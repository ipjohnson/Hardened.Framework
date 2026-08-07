using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Validation;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Validation;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Errors;

/// <summary>
/// This converter decides both the status code a caller sees and how much of the
/// exception is echoed back to them, so its mapping is worth pinning precisely.
/// </summary>
public class ExceptionToModelConverterTests {

    private static readonly ExceptionToModelConverter Converter = new();

    private static IExecutionContext Context() => Substitute.For<IExecutionContext>();

    [Fact]
    public void ValidationExceptionMapsTo400WithFieldErrors() {
        var result = new ValidationResult();
        result.AddError("email", "Required", "email is required");
        result.AddError("age", "Range", "age must be between 0 and 120");

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
            Context(), new ValidationException(new ValidationResult()));

        Assert.Equal(400, status);
        Assert.Empty(Assert.IsType<RequestValidationError>(model).Errors);
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
}
