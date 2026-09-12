# Validation

A constraint on a model becomes a generated validator. A request that fails answers 400 before the
handler runs, with one entry per failed field, and the handler only ever sees values that passed.
There is no validator class and nothing to register.

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

## Declaring constraints in code

The attributes live in the `ValidationModules.Constraints` namespace. Every bounded attribute takes
named `Min` and `Max` arguments, so a single bound is one argument.

| Attribute | Checks |
|---|---|
| `[Required]` | the member was sent |
| `[StringLength(Min, Max)]` | a string's length |
| `[Range(Min, Max)]` | a numeric bound. `ExclusiveMin` and `ExclusiveMax` open the ends |
| `[Pattern(regex)]` | a regular expression |
| `[ItemCount(Min, Max)]` | a collection's size |
| `[MultipleOf]` | divisibility |
| `[AllowedValues]` | membership of a set |

`[Required]` on a non-nullable value type is unnecessary and raises `HRDV003`.

## Which members are required

A constructor parameter of a non-nullable reference type is required. A constructor default makes
one optional.

```csharp
public record NewTodo(string Title);        // {} is a 400: title is required
public record Draft(string? Title);         // {} is accepted, Title is null
public record Paged(string Cursor = "");    // {} is accepted, Cursor is ""
```

`RequiredMemberPresence` marks those members on `System.Text.Json`'s metadata, so the deserializer
refuses the body and no constraint runs. It names every missing member at once.

```json
{ "errors": [
  { "field": "body.accountId", "code": "required", "message": "accountId is required." },
  { "field": "body.weightKg",  "code": "required", "message": "weightKg is required." } ] }
```

A body that both omits a required member and breaks a constraint on a member it did send is
answered about the omission alone. The constraint is reported on the next request.

The published document reads `required` from the same rule, so `required: ["title"]` and the
server's refusal come from one declaration. `[ResponseOnly]` excludes a member the server owns from
both.

### Members nothing checks

Two kinds of member are published as required and are not presence-checked. Declare `[Required]`,
`required`, or `[JsonRequired]` to have one enforced.

```csharp
public class Manifest {
    public string Id { get; set; } = "";        // optional, and the document says so
    public string Carrier { get; set; }         // the document says required; nothing checks it
}
```

A settable property is never presence-checked, whatever its type, because an initializer is how a
property says what to use when nothing sends it. A non-nullable value type is never
presence-checked either, and the document publishes one as required because C# cannot omit it.

A contract-first model needs none of this. A contract's `required` compiles to `[Required]`
whatever the member's type.

## Constraints on a handler's parameters

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

A failure is reported under the name the caller sent, `precision` or `X-Region`, in the envelope a
body failure uses.

A route constraint and a value constraint on the same token answer differently. `/rates/abc` matches
no route and is a 404. `/rates/0` matches, reaches the filter, and is a 400.

`When` and `Unless` name a member of the model the constraint sits on, so a parameter cannot carry
either. `HRDV005` says so at build time.

## Nested models

A constraint on a member of a nested model runs only when the member carries `[ValidateNested]`:

```csharp
public record Address([property: StringLength(Min = 1, Max = 8)] string Postcode);

public record NewJob(
    [property: Required] string Title,
    [property: ValidateNested] Address Pickup);
```

Without it the nested constraints compile and never run, and `HRDV004` reports the member and the
type. The nested record must be sealed or declare a polymorphism mode, or ValidationModules reports
`VM1503`.

## Declaring constraints in a contract

A contract-first application declares constraints as ordinary OpenAPI facets, on schema properties
and on parameters alike. A Smithy model uses `@length`, `@range` and `@pattern` the same way.

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
validation generator reads those as it reads attributes you wrote by hand:

| Contract facet | Generated attribute |
|---|---|
| `minLength` / `maxLength` | `[StringLength]` |
| `minimum` / `maximum` | `[Range]` |
| `exclusiveMinimum` / `exclusiveMaximum` | `[Range(..., ExclusiveMin = true)]` |
| `pattern` | `[Pattern]` |
| `enum` | `[AllowedValues]`, or the generated enum type |
| `minItems` / `maxItems` | `[ItemCount]` |
| `required` | `[Required]` |

A bound you leave out is left out. `minimum: 1` with no `maximum` generates `[Range(Min = 1)]`, and
the failure message says "at least 1".

## The 400 envelope

The generated filter answers 400 with one entry per failed field, in the shape at the top of this
page. A value that fails to parse as its declared type takes the same shape: `?limit=abc` against an
`int` parameter is reported under `limit`, not a 500. A body that omitted a required member is
reported under the object that was missing it, `body.lines[0].sku` rather than `body.sku`.

A body `System.Text.Json` could not read at all is reported against the body:

| Sent | Field | Code |
|---|---|---|
| nothing, or an empty body | `body` | `required` |
| `null` | `body` | `required` |
| `{"weightKg":`, a document that does not parse | `body` | `invalid` |
| `{"weightKg":"heavy"}`, a value that would not convert | `body.weightKg` | `invalid` |

The field is the handler's own body parameter identifier, so a handler taking `MemberRequest
request` reports `request`. On the third row the path names where parsing stopped rather than what
is wrong: `{"accountId":` is reported as `body.accountId`.

## What the document publishes

The published document repeats every declared constraint as the facet it came from, so a client is
validated against the rule the document advertises. An operation with a generated validator also
publishes the 400, with the envelope's schema, under `components.schemas.RequestValidationError`.
See [The OpenAPI document](/guide/openapi-document).

An operation the router already guards publishes no 400. A constraint on a path token is a
[route constraint](/guide/routing#constraining-what-a-token-matches), tested before any filter or
binder runs.

| Declaration | Refused by | Published |
|---|---|---|
| `pattern` on a path token | the router | 404, with no body |
| `{id:int}` with an `int` parameter | the router | 404, with no body |
| `required` on a path token | the router, as a route that did not match | 404, with no body |
| `pattern` on a query value or a header | the validator | 400, the envelope |
| `minimum` on a path token | the validator | 400, the envelope |
| a parameter whose type the constraint does not cover, `{id:long}` binding an `int` | the binder | 400, the envelope |

## Rules the vocabulary cannot express

A business rule is handler code. Throw the exception the generated filters throw and the response is
indistinguishable from a declared constraint's:

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
`ValidationResult.FromErrors`.

## Refusing with a declared status

A well-formed request refused for a reason of its own usually wants a status of its own. Throw the
response for that status and declare it, so the document says so:

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

Without `[Throws<T>]` the throw still answers 409 and the document describes only the 200, so a
client generated from it has no case for the refusal. See
[Declaring what a handler throws](/guide/responses#declaring-what-a-handler-throws). Keep the
validation envelope for "this field is wrong".

## Choosing the status and the shape

`ExceptionResponseSerializer` asks `IExceptionToModelConverter` for the status and the body of every
failure. The stock converter registers with `RegistrationType.Try`, so a registration the
application makes wins.

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
400, the anonymous 500. Two validation exception types reach the converter and the stock one maps
both: Hardened's `ValidationException`, thrown by the generated filters and the binder, and
ValidationModules' own, thrown by code calling a generated validator directly.

## Next

- [The OpenAPI document](/guide/openapi-document): what publishes, including the synthesized 400
- [Declared responses](/guide/responses): declaring the statuses a handler answers deliberately
- [Parameter binding](/guide/parameter-binding): how values reach the validated model
