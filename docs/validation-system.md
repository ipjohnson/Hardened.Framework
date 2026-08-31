# Validation System Design

> **Status.** Parts of this document describe design that is not built.
> `ICustomRequestValidator<TRequest>` exists nowhere in the source: declared constraints are
> enforced by generated filters, and the response they produce is shaped by replacing
> `IExceptionToModelConverter` (register an ordinary `[SingletonService]` implementation; the
> stock converter registers with `RegistrationType.Try` and yields). Business-logic validation
> beyond the constraint vocabulary is handler code today.

## Overview

The Hardened.Framework validation system enforces OpenAPI-defined constraints at runtime. It runs as a filter in the request pipeline after parameter deserialization but before user filters and endpoint invocation.

## Architecture

### Filter Pipeline Position

```
Order -1000  HandlerCreation (instance filter)
Order     5  Serialization (IO filter — deserializes/binds parameters)
Order     6  Validation (validates deserialized parameters)
Order  1000  DefaultValue (user filters)
Order  2000  EndPointInvoke (calls the endpoint method)
```

The `ValidationFilter` runs at `FilterOrder.Validation` (= `Serialization + 1` = 6), ensuring parameters are already deserialized and bound before validation occurs.

### Components

#### Abstractions (`Hardened.Requests.Abstract`)

- **`ValidationError`** — Immutable record: `(string Field, string Code, string Message)`.
- **`ValidationResult`** — Mutable accumulator with `AddError()`, `Merge()`, `IsValid`, and `Errors`. A static `Success` singleton avoids allocations on the happy path.
- **`IValidationRule`** — Stateless, reusable rule: `void Validate(string parameterName, object? value, ValidationResult result)`.
- **`ICustomRequestValidator<TRequest>`** — User-implemented interface for business-logic validation, resolved from DI.

#### Runtime Engine (`Hardened.Requests.Runtime`)

- **Built-in Rules** — `RequiredRule`, `StringLengthRule`, `RangeRule`, `PatternRule`, `EnumRule`, `ArrayBoundsRule`.
- **`ValidationFilter`** — Core `IExecutionFilter` that:
  1. Validates top-level parameters (path/query/header) via `IExecutionRequestParameters.TryGetParameter()`.
  2. Validates request body properties via generated typed accessors (no reflection).
  3. Resolves and invokes `ICustomRequestValidator<T>` instances from DI.
  4. On failure: sets 400 status + `ValidationErrorModel` response, skips `chain.Next()`.
  5. On success: calls `chain.Next()`.
- **`ValidationErrorModel`** — Structured 400 response with per-field errors.
- **`ValidationException`** — Extends `BadRequestException` with a `ValidationResult` property.

#### Source Generator (`Hardened.OpenApi.SourceGenerator`)

- Parses validation constraints (`minLength`, `maxLength`, `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum`, `pattern`, `minItems`, `maxItems`, `required`, `enum`) from OpenAPI specs.
- Emits per-operation `IRequestFilterProvider` classes that construct `ValidationFilter` instances with:
  - `ParameterValidationInfo[]` for path/query/header parameters.
  - `PropertyValidationInfo[]` for request body properties, using **generated typed lambdas** (`body => ((T)body).Property`) — zero reflection at runtime.
- Wires generated filter providers into handler construction via `AttributeModel` entries.

## Error Response Format

```json
{
  "type": "ValidationError",
  "message": "One or more validation errors occurred.",
  "errors": [
    {
      "field": "name",
      "code": "required",
      "message": "name is required."
    },
    {
      "field": "age",
      "code": "range",
      "message": "age must be between 0 and 150."
    }
  ]
}
```

## Custom Validators

Users implement `ICustomRequestValidator<TRequest>` and register via `[TransientService]`. Multiple validators per request type are supported. They run after built-in rule validation.

```csharp
[TransientService]
public class CreatePetValidator : ICustomRequestValidator<CreatePetRequest> {
    public Task<ValidationResult> ValidateAsync(CreatePetRequest request) {
        var result = new ValidationResult();
        if (request.Name == "forbidden") {
            result.AddError("name", "business_rule", "This pet name is not allowed.");
        }
        return Task.FromResult(result);
    }
}
```
