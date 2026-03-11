using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Validation;

public class ValidationFilter : IExecutionFilter {
    private readonly ParameterValidationInfo[] _parameterRules;
    private readonly PropertyValidationInfo[]? _bodyPropertyRules;
    private readonly string? _bodyParameterName;
    private readonly Type? _requestBodyType;

    public ValidationFilter(
        ParameterValidationInfo[] parameterRules,
        PropertyValidationInfo[]? bodyPropertyRules = null,
        string? bodyParameterName = null,
        Type? requestBodyType = null) {
        _parameterRules = parameterRules;
        _bodyPropertyRules = bodyPropertyRules;
        _bodyParameterName = bodyParameterName;
        _requestBodyType = requestBodyType;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var parameters = context.Request.Parameters;
        var result = new ValidationResult();

        // 1. Validate top-level parameters (path/query/header)
        if (parameters != null) {
            foreach (var paramInfo in _parameterRules) {
                parameters.TryGetParameter(paramInfo.ParameterName, out var value);

                foreach (var rule in paramInfo.Rules) {
                    rule.Validate(paramInfo.ParameterName, value, result);
                }
            }
        }

        // 2. Validate body properties via generated typed accessors
        if (_bodyPropertyRules != null && _bodyParameterName != null && parameters != null) {
            if (parameters.TryGetParameter(_bodyParameterName, out var bodyValue) && bodyValue != null) {
                foreach (var propInfo in _bodyPropertyRules) {
                    object? propertyValue;
                    try {
                        propertyValue = propInfo.Accessor(bodyValue);
                    } catch {
                        propertyValue = null;
                    }

                    foreach (var rule in propInfo.Rules) {
                        rule.Validate(propInfo.PropertyName, propertyValue, result);
                    }
                }
            }
        }

        // 3. Resolve and run ICustomRequestValidator<T> from DI
        if (_requestBodyType != null && parameters != null) {
            if (parameters.TryGetParameter(_bodyParameterName!, out var bodyValue) && bodyValue != null) {
                var validatorType = typeof(ICustomRequestValidator<>).MakeGenericType(_requestBodyType);
                var validators = context.RequestServices.GetServices(validatorType);

                foreach (var validator in validators) {
                    if (validator == null) continue;

                    var method = validatorType.GetMethod("ValidateAsync");
                    if (method != null) {
                        var task = (Task<ValidationResult>?)method.Invoke(validator, new[] { bodyValue });
                        if (task != null) {
                            var customResult = await task;
                            result.Merge(customResult);
                        }
                    }
                }
            }
        }

        // 4. If errors: set 400 + ValidationErrorModel, return
        if (!result.IsValid) {
            var errorModel = new ValidationErrorModel {
                Type = "ValidationError",
                Message = "One or more validation errors occurred.",
                Errors = result.Errors.Select(e => new ValidationFieldError {
                    Field = e.Field,
                    Code = e.Code,
                    Message = e.Message
                }).ToList()
            };

            context.Response.Status = 400;
            context.Response.ResponseValue = errorModel;
            return;
        }

        // 5. If valid: continue the chain
        await chain.Next();
    }
}
