using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Validation;

namespace Hardened.Requests.Runtime.Errors;

[SingletonService(Using = RegistrationType.Try)]
public class ExceptionToModelConverter : IExceptionToModelConverter {
    public (int, object) ConvertExceptionToModel(IExecutionContext context, Exception exp) {
        if (exp is ValidationException validationException) {
            var errorModel = new RequestValidationError {
                Type = "ValidationError",
                Message = validationException.Message,
                Errors = validationException.ValidationResult.Errors.Select(e => new RequestValidationFieldError {
                    Field = e.Field,
                    Code = e.Code,
                    Message = e.Message
                }).ToList()
            };
            return (400, errorModel);
        }

        var statusCode = 500;
        var model = new ErrorModel { Type = exp.GetType().Name, Message = exp.Message };

        if (exp.GetType().Name.Contains("Validation") ||
            exp.GetType().Name.Contains("Bad") ||
            exp is FormatException) {
            statusCode = 400;
        }

        return (statusCode, model);
    }
}