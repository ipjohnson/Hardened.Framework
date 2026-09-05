# Validation

A constraint on a model becomes a generated validator. A request that fails answers 400 before the
handler runs, with one entry per failed field, and the handler only ever sees values that passed.

```csharp
using ValidationModules.Constraints;

public record CreatePetRequest(
    [property: StringLength(Min = 1, Max = 100)] string Name,
    [property: Range(Min = 0, Max = 30)] int Age,
    [property: Pattern("^[a-zA-Z0-9-]+$")] string? Tag,
    [property: ItemCount(Max = 5)] List<string>? Nicknames);
```

```json
{
  "type": "ValidationError",
  "message": "One or more validation errors occurred.",
  "errors": [
    { "field": "body.name", "code": "required", "message": "name is required." },
    { "field": "body.age", "code": "range", "message": "age must be between 0 and 30." }
  ]
}
```

There is no validator class to write and no registration to remember. Declaring the constraint is
the whole of it.

## Declaring constraints in code

The attributes live in the `ValidationModules.Constraints` namespace. Every bounded attribute
takes named `Min` and `Max` arguments, so a single bound is one argument. `[Required]` marks a
member the caller must send. On a non-nullable value type it is unnecessary, because absence is
unrepresentable there.

| Attribute | Checks |
|---|---|
| `[Required]` | the member was sent |
| `[StringLength(Min, Max)]` | a string's length |
| `[Range(Min, Max)]` | a numeric bound. `ExclusiveMin` and `ExclusiveMax` open the ends |
| `[Pattern(regex)]` | a regular expression |
| `[ItemCount(Min, Max)]` | a collection's size |
| `[MultipleOf]` | divisibility |
| `[AllowedValues]` | membership of a set |

The same attributes go on a handler's own parameters, for a value bound from the query string, a
header or the path:

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
questions: `/rates/abc` matches no route and is a 404, while `/rates/0` reaches the handler's
filter and is a 400.

A parameter's constraint cannot carry a `When` or `Unless`, which names a member of the model the
constraint sits on. A parameter sits on no model, and `HRDV005` says so at build time.

## Declaring constraints in a contract

A contract-first application declares constraints as ordinary OpenAPI facets, on schema
properties and on parameters alike. A Smithy model uses `@length`, `@range` and `@pattern` the
same way.

```yaml
components:
  schemas:
    CreatePetRequest:
      type: object
      required: [name]
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
          items: { type: string }
          maxItems: 5
```

The build task turns each facet into the matching attribute on the generated model, and the
validation generator reads those exactly as it reads attributes you wrote by hand:

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

## The 400 envelope

A failed request never reaches the handler. The generated filter answers 400 with one entry per
failed field, in the shape at the top of this page. A value that fails to parse as its declared
type takes the same shape: `?limit=abc` against an `int` parameter answers this envelope with
`limit` as the field, not a 500.

## What the document says

The published OpenAPI document repeats every declared constraint as the facet it came from, so the
rule a client is validated against is the one the document advertises. Operations with generated
validators also publish the 400 itself, with the envelope's schema, under
`components.schemas.RequestValidationError`. See [The OpenAPI document](/guide/openapi-document).

## Rules the vocabulary cannot express

A business rule is handler code. Throw the same exception the generated filters throw and the
response is indistinguishable from a declared constraint's:

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

## Refusing with a declared status

When the request was well-formed and refused for a reason of its own, the answer usually wants a
status of its own rather than a 400. Throw the response for that status, and declare it so the
document says so:

```csharp
[Post("/pets")]
[Throws<Conflict<Problem>>]
public async Task<Pet> CreatePet(CreatePetRequest body) {
    if (await _pets.NameExists(body.Name)) {
        throw new Conflict<Problem>(
            new Problem { Detail = $"A pet named '{body.Name}' already exists." }).AsException();
    }

    ...
}
```

`[Throws<T>]` puts the status in the published document. Without it the throw still answers 409
and the document describes only the 200, so a client generated from it has no case for the
refusal. See [Declaring what a handler throws](/guide/responses#declaring-what-a-handler-throws).
Keep the validation envelope for "this field is wrong".

## Choosing the status and the shape

The stock behaviour is a 400 with the envelope above. Both are decided in one replaceable service:
`ExceptionResponseSerializer` asks `IExceptionToModelConverter` for the status and the body of
every failure, and the stock converter registers with `RegistrationType.Try`, so a registration
the application makes wins.

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

Delegating to the stock converter keeps every other mapping: thrown declared statuses, the binding
400, the anonymous 500. Two validation exception types reach the converter, and the stock one maps
both: Hardened's `ValidationException`, thrown by the generated filters and the binder, and
ValidationModules' own, thrown by code calling a generated validator directly. A converter that
should catch both matches on each type the way the stock converter does.

## Next

- [The OpenAPI document](/guide/openapi-document): what publishes, including the synthesized 400
- [Declared responses](/guide/responses): declaring the statuses a handler answers deliberately
- [Parameter binding](/guide/parameter-binding): how values reach the validated model
