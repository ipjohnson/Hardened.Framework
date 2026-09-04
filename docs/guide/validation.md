# Validation

A constraint you declare becomes a generated validator. The build reads constraints from the
contract or from attributes, emits the validation code, and registers it. A request that fails
answers 400 before the handler runs, with a field-level error list. The handler only ever sees
values that passed.

There is no validator base class to write and no registration to remember. Declaring the
constraint is the whole of it.

## Declaring constraints in a contract

Contract-first applications declare constraints as ordinary OpenAPI facets, on schema properties
and on parameters alike. Smithy models use `@length`, `@range` and `@pattern` the same way.

```yaml
components:
  schemas:
    CreatePetRequest:
      type: object
      required:
        - name
      properties:
        name:
          type: string
          minLength: 1
          maxLength: 100
        age:
          type: integer
          minimum: 0
          maximum: 30
        tag:
          type: string
          pattern: "^[a-zA-Z0-9-]+$"
        nicknames:
          type: array
          items:
            type: string
          maxItems: 5
```

The build task turns each facet into the matching attribute from `ValidationModules.Constraints`
on the generated model, and the validation generator reads those exactly as it reads attributes
you wrote by hand. One vocabulary serves both directions.

| Contract facet | Generated attribute |
|---|---|
| `minLength` / `maxLength` | `[StringLength]` |
| `minimum` / `maximum` | `[Range]` |
| `exclusiveMinimum` / `exclusiveMaximum` | `[Range(..., ExclusiveMin = true)]` |
| `pattern` | `[Pattern]` |
| `enum` | `[AllowedValues]`, or the generated enum type |
| `minItems` / `maxItems` | `[ItemCount]` |
| `required` | `[Required]` |

A bound you leave out is left out. `minimum: 1` with no `maximum` generates `[Range(Min = 1)]`,
and the failure message says "at least 1" without inventing an upper bound.

## Declaring constraints in code

Code-first models carry the same attributes directly. They live in the
`ValidationModules.Constraints` namespace:

```csharp
using ValidationModules.Constraints;

public record CreatePetRequest(
    [property: StringLength(Min = 1, Max = 100)] string Name,
    [property: Range(Min = 0, Max = 30)] int Age,
    [property: Pattern("^[a-zA-Z0-9-]+$")] string? Tag,
    [property: ItemCount(Max = 5)] List<string>? Nicknames);
```

Every bounded attribute takes named `Min` and `Max` arguments, so a single bound is one argument.
`[Required]` marks a member the caller must send; on a non-nullable value type it is unnecessary,
because absence is unrepresentable there.

The same attributes go on a handler's own parameters, for a value bound from the query string, a
header, the path or a cookie:

```csharp
[Get("/rates/{count:int}")]
public int Page([Range(Min = 1, Max = 100)] int count) => count;

[Get("/precision")]
public int Precision([FromQueryString] [Range(Min = 2, Max = 8)] int precision) => precision;

[Get("/region")]
public string Region([FromHeader("X-Region")] [StringLength(2, 2)] string region) => region;
```

A failure is reported under the name the caller sent, `precision` or `X-Region`, in the same
envelope a body failure uses. A route constraint and a value constraint answer different
questions: `/rates/abc` matches no route and is a 404, `/rates/0` reaches the handler's filter
and is a 400. The one thing a parameter's constraint cannot carry is a `When` or `Unless`, which
names a member of the model the constraint sits on; a parameter sits on no model, and `HRDV005`
says so at build time.

## The 400 envelope

A failed request never reaches the handler. The generated filter answers 400 with one entry per
failed field:

```json
{
  "type": "ValidationError",
  "message": "One or more validation errors occurred.",
  "errors": [
    {
      "field": "body.name",
      "code": "required",
      "message": "name is required."
    },
    {
      "field": "body.age",
      "code": "range",
      "message": "age must be between 0 and 30."
    }
  ]
}
```

A value that fails to parse as its declared type takes the same shape. `?limit=abc` against an
`int` parameter answers this envelope with `limit` as the field, not a 500.

## What the document says

The published OpenAPI document repeats every declared constraint as the facet it came from, so
the rule a client is validated against is the one the document advertises. Operations with
generated validators also publish the 400 itself, with the envelope's schema, under
`components.schemas.RequestValidationError`. See
[The OpenAPI document](/guide/openapi-document).

## Validating in handler code

Business rules beyond the constraint vocabulary are handler code. Throw the same exception the
generated filters throw and the response is indistinguishable from a declared constraint's:

```csharp
using Hardened.Requests.Runtime.Validation;
using ValidationModules;

public async Task<Pet> CreatePet(CreatePetRequest body) {
    if (await _pets.NameExists(body.Name)) {
        throw new ValidationException(ValidationResult.FromErrors([
            new ValidationError("name", "duplicate", "A pet with this name already exists.")
        ]));
    }

    ...
}
```

`ValidationResult` is ValidationModules' immutable result type. Build it with
`ValidationResult.FromErrors`; there is no mutable `AddError` form.

## Choosing the status and the shape

The stock behaviour is a 400 with the envelope above. Both are decided in one replaceable
service: `ExceptionResponseSerializer` asks `IExceptionToModelConverter` for the status and the
body of every failure, and the stock converter registers with `RegistrationType.Try`, which
yields to any registration the application makes. Registering your own converter is the supported
way to answer validation failures with a different status or a different shape. No configuration
switch exists because the seam is the switch.

A converter that answers 422 with its own body, and leaves everything else to the stock rules:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Validation;

public record FieldProblem(string Name, string Reason);

public record UnprocessableContent(string Title, IReadOnlyList<FieldProblem> Fields);

[SingletonService]
public class UnprocessableContentConverter : IExceptionToModelConverter {
    private readonly ExceptionToModelConverter _stock = new();

    public (int, object) ConvertExceptionToModel(IExecutionContext context, Exception exp) {
        if (exp is ValidationException validation) {
            return (422, new UnprocessableContent(
                "The request was understood and refused.",
                validation.ValidationResult.Errors
                    .Select(error => new FieldProblem(error.Field, error.Message))
                    .ToList()));
        }

        return _stock.ConvertExceptionToModel(context, exp);
    }
}
```

Delegating to the stock converter keeps every other mapping: thrown declared statuses, the
binding 400, the anonymous 500. Handle only the exceptions whose answer you want to change.

Two validation exception types reach the converter, and the stock one maps both: Hardened's
`ValidationException` (thrown by the generated filters and the binder) and ValidationModules'
own, thrown by code calling a generated validator directly. A custom converter that should catch
both matches on each type the way the stock converter does.

## Next

- [The OpenAPI document](/guide/openapi-document) — what publishes, including the synthesized 400
- [Declared responses](/guide/responses) — declaring the statuses a handler answers deliberately
- [Parameter binding](/guide/parameter-binding) — how values reach the validated model
