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

        var model = new ErrorModel { Type = exp.GetType().Name, Message = exp.Message };

        // Client errors are identified by type, not by the shape of the type's name.
        //
        // This previously matched any exception whose name contained "Validation" or "Bad",
        // which classified unrelated types - a BadgeNotFoundException became a 400 - while
        // missing any client error that happened not to be named that way.
        //
        // To have an exception treated as a client error, derive it from
        // BadRequestException.
        var statusCode = exp is BadRequestException or FormatException ? 400 : 500;

        return (statusCode, model);
    }
}