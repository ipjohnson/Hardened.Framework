using System.Text.Json;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Validation;

namespace Hardened.Requests.Runtime.Errors;

[SingletonService(Using = RegistrationType.Try)]
public class ExceptionToModelConverter : IExceptionToModelConverter {
    public (int, object) ConvertExceptionToModel(IExecutionContext context, Exception exp) {
        // Both routes to a validation failure land here. The filter throws Hardened's exception,
        // which is a BadRequestException; a handler calling ValidateAndThrow itself throws
        // ValidationModules'. One mapper, rather than two shapes that agree by duplication - which
        // is what that type's own documentation asks a framework to do.
        var validationResult = exp switch {
            ValidationException hardened => hardened.ValidationResult,
            ValidationModules.ValidationException validationModules => validationModules.Result,
            _ => null
        };

        if (validationResult != null) {
            var errorModel = new RequestValidationError {
                Type = "ValidationError",
                Message = exp.Message,
                Errors = validationResult.Errors.Select(e => new RequestValidationFieldError {
                    Field = e.Field,
                    Code = e.Code,
                    Message = e.Message
                }).ToList()
            };
            return (400, errorModel);
        }

        // An exception that names its own status, which is how a specification's declared error
        // responses reach the wire. Checked before the type-based classification below, because a
        // declared 404 is more specific than "not a BadRequestException, so 500".
        //
        // Matched on the interface rather than on StatusCodeException, so that a status carrying a
        // header - a 401 and its WWW-Authenticate challenge - gets to add one. The body still comes
        // from StatusCodeException.Value when the exception is one, which keeps a declared payload
        // working exactly as it did.
        if (exp is IStatusCodeException statusCodeException) {
            statusCodeException.ApplyHeaders(context.Response.Headers);

            var declaredValue = exp is StatusCodeException { Value: { } value } ? value : null;

            return (
                statusCodeException.StatusCode,
                declaredValue ?? new ErrorModel {
                    Type = exp.GetType().Name, Message = exp.Message
                });
        }

        // A body the caller sent that this service cannot read. System.Text.Json raises it for
        // malformed JSON, and the generated converters raise it deliberately to name a value the
        // specification does not declare - "'cooking' is not a value Genre declares."
        //
        // Every other bad value in the same body already answers 400 with a field-level error list;
        // this one answered 500, which told a client its own typo was a server fault. Shaped as a
        // validation error rather than an ErrorModel so one malformed field reads the same however
        // it was caught.
        if (exp is JsonException jsonException) {
            return (400, BodyReadError(jsonException));
        }

        // Client errors are identified by type, not by the shape of the type's name.
        //
        // This previously matched any exception whose name contained "Validation" or "Bad",
        // which classified unrelated types - a BadgeNotFoundException became a 400 - while
        // missing any client error that happened not to be named that way.
        //
        // To have an exception treated as a client error, derive it from
        // BadRequestException.
        if (exp is BadRequestException or FormatException) {
            // The message is kept here and dropped below, which is the whole distinction: these are
            // raised about the caller's own request, by code that chose the wording for them.
            return (400, new ErrorModel { Type = exp.GetType().Name, Message = exp.Message });
        }

        // Nothing about the exception reaches the caller.
        //
        // This used to answer with the type's name and its message verbatim, and a test pinned that
        // - using "connection string 'Server=db;Password=hunter2' failed" as its example, so the
        // hazard was understood at the time and left for a deliberate decision. This is it. An
        // unhandled exception is a server fault; its message was written for whoever is reading the
        // logs, not for whoever made the request, and it is the one message here nobody chose with a
        // caller in mind.
        //
        // Nothing is lost: IRequestLogger.RequestFailed already logs the exception with its stack,
        // method and path at Error, which is where it belongs.
        return (500, ServerError);
    }

    /// <summary>
    /// The whole of what a caller learns from an unhandled exception.
    /// </summary>
    /// <remarks>
    /// Shared rather than constructed per request - it holds nothing about the request, which is
    /// the point of it.
    /// </remarks>
    private static readonly ErrorModel ServerError = new() {
        Type = "ServerError",
        Message = "The server could not complete this request."
    };

    /// <summary>
    /// A body that could not be read, as the same field-level shape a failed constraint produces.
    /// </summary>
    /// <remarks>
    /// The message is the exception's, because a <c>JsonException</c> describes the caller's own
    /// payload - which is what a 400 is for, and what every constraint message in the same list
    /// already does. Its trailing <c>Path: $.x | LineNumber: 0 | ...</c> is dropped, since the path
    /// is the field rather than prose.
    /// </remarks>
    private static RequestValidationError BodyReadError(JsonException exception) =>
        new() {
            Type = "ValidationError",
            Message = "One or more validation errors occurred.",
            Errors = [
                new RequestValidationFieldError {
                    Field = FieldFrom(exception.Path),
                    Code = "invalid",
                    Message = WithoutPositionSuffix(exception.Message)
                }
            ]
        };

    /// <summary>
    /// <c>$.genre</c>, as <c>body.genre</c> - the spelling the constraint validators use.
    /// </summary>
    private static string FieldFrom(string? path) {
        if (string.IsNullOrEmpty(path) || path == "$") {
            return "body";
        }

        return path!.StartsWith("$.", StringComparison.Ordinal)
            ? "body." + path.Substring(2)
            : "body" + path.Substring(1);
    }

    private static string WithoutPositionSuffix(string message) {
        var marker = message.IndexOf(" Path: ", StringComparison.Ordinal);

        return marker == -1 ? message : message.Substring(0, marker);
    }
}