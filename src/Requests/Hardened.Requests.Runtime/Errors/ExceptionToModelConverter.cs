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

            // The status the contract declared for validation failures, where it declared one.
            // Arm C published a 422 and was answered 400; the declared status was wired to
            // nothing.
            return (context.HandlerInfo?.ValidationErrorStatus ?? 400, errorModel);
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
            return (400, BodyReadError(jsonException, BodyField(context)));
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
    private static RequestValidationError BodyReadError(JsonException exception, string body) =>
        new() {
            Type = "ValidationError",
            Message = "One or more validation errors occurred.",
            Errors = MissingMembers(exception.Message, body) ?? [
                new RequestValidationFieldError {
                    Field = FieldFrom(exception.Path, body),
                    Code = "invalid",
                    Message = WithoutPositionSuffix(exception.Message)
                }
            ]
        };

    /// <summary>
    /// The prefix a body field is reported under: the handler's own parameter identifier, which is
    /// what the generated validators use. "body" only where nothing says otherwise.
    /// </summary>
    private static string BodyField(IExecutionContext context) {
        var name = context.HandlerInfo?.BodyParameterName;

        return string.IsNullOrEmpty(name) ? "body" : name!;
    }

    /// <summary>
    /// The members a required-member failure names, as the errors <c>[Required]</c> would have
    /// produced - or null where this is some other <c>JsonException</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the deserializer reports this at all.</b> A required member of a value type carries no
    /// <c>[Required]</c>: the validation generator emits <c>value.x is null</c>, which is CS0037
    /// against an <c>int</c>, so the constraint is suppressed and the validator never sees the
    /// absence. <c>JsonTypeInfoEmitter</c> marks those members required on the deserializer instead,
    /// and this is where the answer is shaped. Without it, an omitted enum silently became its first
    /// declared member and the API answered 201 with a value the caller never sent.
    /// </para>
    /// <para>
    /// <b>Indistinguishable from the validator's own answer, deliberately.</b> Same field spelling,
    /// same <c>required</c> code, same <c>"{field} is required."</c> wording - so which layer caught
    /// a missing member is this framework's business and not the caller's. System.Text.Json
    /// aggregates, listing every member it missed, so the list is complete rather than first-only.
    /// </para>
    /// <para>
    /// <b>Read from the message, which is the part worth being uneasy about.</b> There is no typed
    /// exception for this and no structured member list on <c>JsonException</c>; the path is
    /// <c>$</c>, because the object rather than any one member is what failed. So the shape is
    /// matched conservatively and anything unrecognised falls through to the general branch above -
    /// a caller gets a less precise 400, never a wrong one. <c>MissingRequiredMembersMessageTests</c>
    /// pins the .NET behaviour, so an SDK that changes the wording fails a test here rather than
    /// silently degrading in production.
    /// </para>
    /// </remarks>
    private static List<RequestValidationFieldError>? MissingMembers(string message, string body) {
        const string prefix = "JSON deserialization for type ";
        const string marker = "missing required properties";

        if (!message.StartsWith(prefix, StringComparison.Ordinal) ||
            message.IndexOf(marker, StringComparison.Ordinal) == -1) {
            return null;
        }

        var listStart = message.IndexOf(": ", message.IndexOf(marker, StringComparison.Ordinal),
            StringComparison.Ordinal);

        if (listStart == -1) {
            return null;
        }

        var errors = new List<RequestValidationFieldError>();

        // The list System.Text.Json writes joins with the current culture's list separator - a
        // comma only on most machines - quotes each member, and ends the sentence with a period.
        // Splitting on a literal comma and keeping the decoration is what produced the trial's
        // `body.'code'.` field. Every piece of dressing comes off before the member is a field.
        var separator = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator;

        var names = separator == ","
            ? message.Substring(listStart + 2).Split(',')
            : message.Substring(listStart + 2).Split([separator, ","], StringSplitOptions.None);

        foreach (var name in names) {
            var member = name.Trim().TrimEnd('.').Trim('\'', '"');

            if (member.Length == 0) {
                continue;
            }

            var field = body + "." + member;

            errors.Add(new RequestValidationFieldError {
                Field = field, Code = "required", Message = field + " is required."
            });
        }

        return errors.Count == 0 ? null : errors;
    }

    /// <summary>
    /// <c>$.genre</c>, as <c>body.genre</c> - the spelling the constraint validators use, under
    /// the handler's own parameter identifier.
    /// </summary>
    private static string FieldFrom(string? path, string body) {
        if (string.IsNullOrEmpty(path) || path == "$") {
            return body;
        }

        return path!.StartsWith("$.", StringComparison.Ordinal)
            ? body + "." + path.Substring(2)
            : body + path.Substring(1);
    }

    private static string WithoutPositionSuffix(string message) {
        var marker = message.IndexOf(" Path: ", StringComparison.Ordinal);

        return marker == -1 ? message : message.Substring(0, marker);
    }
}