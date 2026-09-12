# Validation

Constraint attributes on a model become a generated validator. A request that fails one answers 400
before the handler runs, with an entry per failed field.

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

`Hardened.Validation.SourceGenerator` writes the validator. A project that declares constraints
without referencing it compiles, enforces nothing, and reports `HRDV006`.

## Constraint attributes

The attributes are in `ValidationModules.Constraints`. Bounded attributes take named `Min` and `Max`
arguments.

| Attribute | Checks |
|---|---|
| `[Required]` | the member was sent |
| `[StringLength(Min, Max)]` | a string's length |
| `[Range(Min, Max)]` | a numeric bound. `ExclusiveMin` and `ExclusiveMax` open the ends |
| `[Pattern(regex)]` | a regular expression |
| `[ItemCount(Min, Max)]` | a collection's size |
| `[MultipleOf]` | divisibility |
| `[AllowedValues]` | membership of a set |

`[Required]` on a non-nullable value type reports `HRDV003`.

## Required members

`System.Text.Json` ignores nullable reference annotations. `RequiredMemberPresence` marks a
non-nullable reference member required on the deserializer's metadata instead, so `{}` against
`record NewTodo(string Title)` is refused before any constraint runs. One response names every
missing member.

```json
{ "errors": [
  { "field": "body.accountId", "code": "required", "message": "accountId is required." },
  { "field": "body.weightKg",  "code": "required", "message": "weightKg is required." } ] }
```

A member the model already declares `required` or `[JsonRequired]` is left as it is.

| Member | Published as | Presence checked |
|---|---|---|
| constructor parameter, non-nullable reference type | required | yes |
| constructor parameter with a default value | optional | no |
| property that is not a constructor parameter | required, unless it has an initializer | no |
| any value type | required | no |
| `[ResponseOnly]` | `readOnly` | no |

`[Required]`, `required` or `[JsonRequired]` adds a presence check to any row marked no.

When a body omits a required member and also breaks a constraint, the response names the omission
alone and the constraint comes back on the next request.

An AOT-published application declares presence with `required` or `[JsonRequired]`. Under a
source-generated `JsonSerializerContext` the deserializer reads `IsRequired` from that generator
rather than from reflection.

A contract-first model does not need `RequiredMemberPresence`. A contract's `required` compiles to
`[Required]` whatever the member's type.

## Constraints on handler parameters

The same attributes go on a handler's parameters, for a value bound from the query string, a header
or the path:

```csharp
using Hardened.Web.Runtime.Attributes;

[Get("/rates/{count:int}")]
public int Page([Range(Min = 1, Max = 100)] int count) => count;

[Get("/precision")]
public int Precision([FromQueryString] [Range(Min = 2, Max = 8)] int precision) => precision;

[Get("/region")]
public string Region([FromHeader("X-Region")] [StringLength(2, 2)] string region) => region;
```

The response names the value the caller sent, `precision` or `X-Region`.

`When` or `Unless` names another member of the model the constraint sits on. Either one on a handler
parameter reports `HRDV005`.

## Nested models

Constraints on a nested model run only where the member carries `[ValidateNested]`:

```csharp
public record Address([property: StringLength(Min = 1, Max = 8)] string Postcode);

public record NewJob(
    [property: Required] string Title,
    [property: ValidateNested] Address Pickup);
```

Without it the nested constraints compile, never run, and report `HRDV004`. The nested type must be
sealed or declare a polymorphism mode, or ValidationModules reports `VM1503`.

## Constraints from a contract

A contract declares constraints as OpenAPI facets on schema properties and parameters. A Smithy
model uses `@length`, `@range` and `@pattern`.

```yaml
components:
  schemas:
    CreatePetRequest:
      type: object
      required: [name]
      properties:
        name: { type: string, minLength: 1, maxLength: 100 }
        age: { type: integer, minimum: 0, maximum: 30 }
        tag: { type: string, pattern: "^[a-zA-Z0-9-]+$" }
        nicknames: { type: array, items: { type: string }, maxItems: 5 }
```

The build task writes the matching attribute onto the generated model, and the validation generator
reads it as it reads a hand-written one.

| Facet | Attribute |
|---|---|
| `minLength` / `maxLength` | `[StringLength]` |
| `minimum` / `maximum` | `[Range]` |
| `exclusiveMinimum` / `exclusiveMaximum` | `[Range(..., ExclusiveMin = true)]` |
| `pattern` | `[Pattern]` |
| `enum` | `[AllowedValues]`, or the generated enum type |
| `minItems` / `maxItems` | `[ItemCount]` |
| `required` | `[Required]` |

`minimum: 1` with no `maximum` generates `[Range(Min = 1)]`, and the message reads "at least 1".

## The 400 response

The generated filter answers the envelope above. A value that does not parse as its declared type
takes the same shape, so `?limit=abc` against an `int` parameter is reported under `limit`. A
nested object carries its own path, so a member missing from one is reported as
`body.lines[0].sku`.

An unreadable body is reported under `body`:

| Sent | Field | Code |
|---|---|---|
| nothing, or an empty body | `body` | `required` |
| `null` | `body` | `required` |
| a document that does not parse | `body` | `invalid` |
| `{"weightKg":"heavy"}` | `body.weightKg` | `invalid` |

`body` is the handler's own parameter identifier, so a handler taking `MemberRequest request`
reports `request`. On the third row the path names where parsing stopped rather than the fault, so
`{"accountId":` is reported as `body.accountId`.

## What the document publishes

The document republishes every constraint as the facet it came from. An operation with a generated
validator also publishes the 400, with its schema under
`components.schemas.RequestValidationError`. See
[The OpenAPI document](/guide/openapi-document).

A constraint on a path token is a [route constraint](/guide/routing#constraining-what-a-token-matches),
tested before any filter runs.

| Declaration | Refused by | Published |
|---|---|---|
| `pattern` on a path token | the router | 404, no body |
| `{id:int}` with an `int` parameter | the router | 404, no body |
| `required` on a path token | the router | 404, no body |
| `pattern` on a query value or header | the validator | 400, the envelope |
| `minimum` on a path token | the validator | 400, the envelope |
| `{id:long}` binding an `int` parameter | the binder | 400, the envelope |

## Custom rules

Write a rule the attributes cannot express in the handler. Throwing `ValidationException` produces
the same response a constraint does:

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

`ValidationResult` is immutable.

## Refusing with another status

A refusal that is not about a field takes its own status. Throw the response for that status and
declare it:

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

Without `[Throws<T>]` the throw answers 409. The document then describes only the 200. See
[Declaring what a handler throws](/guide/responses#declaring-what-a-handler-throws).

## Replacing the status and body

`ExceptionResponseSerializer` calls `IExceptionToModelConverter` for the status and body of every
failure. The stock converter registers with `RegistrationType.Try`, so an application's own
registration replaces it.

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

Delegating to the stock converter keeps thrown declared statuses, the binding 400 and the 500. Two
validation exception types reach it, and the stock converter maps both.

| Exception | Thrown by |
|---|---|
| `Hardened.Requests.Runtime.Validation.ValidationException` | the generated filters and the binder |
| `ValidationModules.ValidationException` | code that calls a generated validator directly |

## Next

- [The OpenAPI document](/guide/openapi-document): the published 400 and its schema
- [Declared responses](/guide/responses): declaring a handler's statuses
- [Parameter binding](/guide/parameter-binding): how a value reaches a validated model
