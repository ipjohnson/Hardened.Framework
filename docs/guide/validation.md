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
member the caller may not send as `null`. On a non-nullable value type it is unnecessary, because
absence is unrepresentable there — and `HRDV003` says so.

### Presence, and who checks it

A non-nullable reference member the constructor takes is one the caller must send. Nothing else
declares it:

```csharp
public record NewTodo(string Title);        // {} is a 400: title is required
public record Draft(string? Title);         // {} is accepted, Title is null
public record Paged(string Cursor = "");    // {} is accepted, Cursor is ""
```

That is the same declaration the published document reads — `required: ["title"]` comes from the
annotation on `Title` and from nothing else — so the document and the server now answer the same
question the same way. A member the server owns is excluded from both by `[ResponseOnly]`.

A settable property is not demanded, whatever its type:

```csharp
public class Manifest {
    public string Id { get; set; } = "";        // optional, and the document says so
    public string Carrier { get; set; }         // the document says required; nothing checks it
}
```

An initializer is how a property says "this when nothing sends it", and it compiles into the
constructor body where reflection cannot see it — so demanding these would refuse bodies their author
meant to accept. The document reads the initializer from source and leaves `id` optional; `carrier`,
which has none, is published as required and is `[Required]`'s to enforce. Write the demand where the
reader can see it — a constructor parameter, `required string Carrier`, or `[JsonRequired]` — or
declare `[Required]` and let the validator answer.

**Presence is the reader's question; content is the validator's.** Absence is the one thing a
validator cannot see, and the one thing the JSON reader knows for certain, so a body that omits a
required member is refused before any constraint runs. Every member it missed is named at once:

```json
{ "errors": [
  { "field": "body.accountId", "code": "required", "message": "accountId is required." },
  { "field": "body.weightKg",  "code": "required", "message": "weightKg is required." } ] }
```

The trade is worth knowing: a body that both omits a required member and breaks a constraint on a
member it did send is answered about the omission, and about the constraint on the next request.
Everything the reader accepted is validated together, as before.

A non-nullable **value** type is not covered by this. The document publishes one as required because
C# serialization cannot omit it — a fact about what a response always contains rather than a demand
its author made, and `int?` or `= 0` are the only ways C# has to say otherwise. To demand one, say
so: `required int Grams`, or `[JsonRequired]`. A contract-first model needs neither, because a
contract's `required` is a demand and is compiled as one whatever the member's type.

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

### Nested models

A constraint on a member of a nested model runs only when the member carries `[ValidateNested]`:

```csharp
public record Address([property: StringLength(Min = 1, Max = 8)] string Postcode);

public record NewJob(
    [property: Required] string Title,
    [property: ValidateNested] Address Pickup);
```

Without it the nested constraints compile and never run, and `HRDV004` says so at build time:
*'NewJob.Pickup' does not declare [ValidateNested] and its Address type declares constraints, so
none of them run and an invalid 'Address' is accepted with no error. Add [ValidateNested] to the
property, or set NoWarn HRDV004 if the skip is intended.* The nested record must be sealed, or
declare a polymorphism mode, or ValidationModules reports `VM1503`.

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
`limit` as the field, not a 500. So does a body that omitted a required member, reported under the
object that was missing it — `body.lines[0].sku`, not `body.sku`. Which layer caught a fault is not
something a caller can tell.

## What the document says

The published OpenAPI document repeats every declared constraint as the facet it came from, so the
rule a client is validated against is the one the document advertises. Operations with generated
validators also publish the 400 itself, with the envelope's schema, under
`components.schemas.RequestValidationError`. See [The OpenAPI document](/guide/openapi-document).

An operation the router already guards publishes no 400. A constraint on a path token is a
[route constraint](/guide/routing#constraining-what-a-token-matches), tested before any filter or
binder runs, so a value that would have failed it is a 404 and the validator never sees one:

| Declaration | Refused by | Published |
|---|---|---|
| `pattern` on a path token | the router | 404, with no body |
| `{id:int}` with an `int` parameter | the router | 404, with no body |
| `required` on a path token | the router, as a route that did not match | 404, with no body |
| `pattern` on a query value or a header | the validator | 400, the envelope |
| `minimum` on a path token | the validator | 400, the envelope |
| a parameter whose type the constraint does not cover, `{id:long}` binding an `int` | the binder | 400, the envelope |

So `pattern` and `minimum` on the same token answer differently, and deliberately: the first says
which URLs name a resource, the second judges a request that named one. Where the operation declares
a 404 of its own, its description says the router answers that status too, and without the declared
body — two 404s reach the wire and a document can key one.

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
