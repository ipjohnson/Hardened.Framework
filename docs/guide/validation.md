# Validation

Hardened compiles constraint attributes into a validator and runs it before the handler. The
request is refused with 400, and the body names every field that failed.

```csharp
using Hardened.Web.Runtime.Attributes;
using ValidationModules.Constraints;

public class RegistrationModel {
    [Required]
    [StringLength(Min = 3, Max = 20)]
    public string? Name { get; set; }

    [Range(18, 120)]
    public int Age { get; set; }
}

[BasePath("/registration")]
public class RegistrationController {

    [Post("/")]
    public string Register(RegistrationModel model) => model.Name ?? "";
}
```

The controller declares no filter and no registration. The constraints are the whole declaration.

## The package

The constraint attributes come from `ValidationModules.Constraints`, which the application already
references. A second package compiles them.

```xml
<PackageReference Include="Hardened.Validation.SourceGenerator" />
```

A project that declares constraints without this package builds clean and enforces nothing. The
build reports `HRDV006` for that case.

## The constraint attributes

Every attribute below lives in `ValidationModules.Constraints`.

| Attribute | Arguments | Applies to | Document facet |
| --- | --- | --- | --- |
| `[Required]` | none | any member | the name in `required` |
| `[StringLength]` | `(min, max)` or `Min =`, `Max =` | a string | `minLength`, `maxLength` |
| `[Range]` | `(min, max)` or `Min =`, `Max =`, `ExclusiveMin =`, `ExclusiveMax =` | a number | `minimum`, `maximum` |
| `[Pattern]` | `("regex")` or `(typeof(T), nameof(T.Member))` | a string | `pattern` |
| `[ItemCount]` | `(min, max)` or `Min =`, `Max =` | a collection | `minItems`, `maxItems` |
| `[AllowedValues]` | one string per permitted value | a string | `enum` |
| `[ValidateNested]` | none | a member with structure | none |

`[MultipleOf]` and `[UniqueItems]` publish `multipleOf` and `uniqueItems`. The contract-first build
task maps neither keyword back into an attribute.

`[Range]` declares its bounds as `object?`, so a decimal bound is written as a string. The build
parses that string against the member's own type.

The generator reads `System.ComponentModel.DataAnnotations` through the same front end. A model
carrying only `[Required]` and `[StringLength(64, MinimumLength = 1)]` gets a validator. Both
vocabularies declare a `Required` and a `Range`, so a model using both needs an alias.

```csharp
using ValidationModules.Constraints;
using DataAnnotations = System.ComponentModel.DataAnnotations;
```

The published OpenAPI document carries facets for the `ValidationModules.Constraints` vocabulary
only. A DataAnnotations constraint runs and is absent from the document.

## What the build generates

`Hardened.Validation.SourceGenerator` reads every type declaration in the compilation. A type whose
members carry a constraint gets a validator class named `<TypeName>Validator`. A type that declares
no constraint gets nothing.

The same generator writes a partial of the entry point class that registers each validator as a
singleton. The file is named `<EntryPoint>.ValidationModule.g.cs`. A compilation with no validator
gets no registration file. A compilation with no entry point still gets its validators.

The web generator and the function generator emit a second validator, for the handler's nested
`Parameters` class, named `<Handler>ParametersValidator`. That validator descends into a body whose
type has a validator. It also checks the constraints written on the handler's own parameters. The
generator then adds `ValidationFilterProvider<...Parameters>` to the handler's filter list.

A handler whose types and parameters constrain nothing gets no filter. A validated handler runs its
filter at `FilterOrder.Validation`, which is one stage after binding and one stage before
authorization.

Two other cases generate no filter. A project without `Hardened.Validation.SourceGenerator`
attaches nothing, because the filter would name a validator that nobody emits. A contract-first
operation already carries `[Validate<I...Parameters>]` from the contract bridge, so the handler
generator leaves it alone.

## Build-time diagnostics

| Code | Severity | Condition | Fix |
| --- | --- | --- | --- |
| `HRDV002` | Error | Two validators claim the same generated file. | Report it. The cause is a defect in the generator. |
| `HRDV003` | Warning | `[Required]` sits on a non-nullable value type. | Declare the member `required`, or add `[JsonRequired]`, or make the type nullable. |
| `HRDV004` | Warning | A property omits `[ValidateNested]`, and its type declares constraints. | Add `[ValidateNested]`, or set `<NoWarn>$(NoWarn);HRDV004</NoWarn>`. |
| `HRDV005` | Error | A constraint on a handler parameter sets `When` or `Unless`. | Remove the condition, or move the constraint onto a model property. |
| `HRDV006` | Warning | The project declares constraints and references no validation generator. | Reference `Hardened.Validation.SourceGenerator`, or remove the constraints. |

`HRDV003` fires because `[Required]` compiles to a null check, and a value type is never null. An
omitted `int` arrives as `0` and passes. An omitted enum arrives as its first declared member, which
reads as a deliberate choice.

`HRDV004` fires only on a parent that constrains something itself. The child type must also be
declared in the same compilation. A child type from a package is left alone.

`HRDV005` fires because `When` and `Unless` name a member of the model the constraint sits on. A
handler parameter sits on no model.

`HRDV006` is reported once per assembly, however many handlers declare constraints.

`HRDV001` is retired. It warned that a constraint on a handler parameter was not compiled, and those
constraints are compiled now.

## The failure response

A failed constraint answers 400 with this body.

```json
{
  "type": "ValidationError",
  "message": "One or more validation errors occurred.",
  "errors": [
    { "field": "model.name", "code": "string_length", "message": "..." },
    { "field": "model.age", "code": "range", "message": "..." }
  ]
}
```

Every failing constraint is reported, not only the first.

| Code | Raised by |
| --- | --- |
| `required` | a `[Required]` on a null value, an absent required member, an empty body, a `null` body |
| `string_length` | `[StringLength]` |
| `range` | `[Range]` |
| `pattern` | `[Pattern]` |
| `invalid` | a value that does not convert to the declared type, or a body that does not parse |

Four layers produce this body, and the caller cannot tell them apart. The generated filter produces
it. The string converter produces it for a query, header or path value that does not parse. The
JSON reader's refusal is reshaped into it. A handler throwing `ValidationException` produces it.

`ExceptionToModelConverter` writes the body, and it is the only place that decides what a validation
failure looks like.

## Field names

A failure inside the body is pathed under the handler's own body parameter name. A handler taking
`RegistrationModel model` reports `model.name`. A contract-first operation reports `body.name`,
because the generated member is called `body`. A failing element of an array carries its index, as
`body.lines[0].sku`.

A query, header or path value is reported bare, under the name the caller sent. A parameter bound by
`[FromHeader("X-Region")]` reports `X-Region`, never the C# identifier. One bound by
`[FromQueryString("p")]` reports `p`.

Property names are wire names. A property called `Reference` reports as `reference`.

## Required members and absent values

Absence is the deserializer's question, and content is the validator's. A validator cannot see an
absent value, because an omitted `int` is indistinguishable from `0` once the model is built.

The deserializer demands a member that is a constructor parameter, is a non-nullable reference type,
and has no default value. It refuses `{}` against `record Todo(string Title)`. It names every
missing member in one answer.

| Member | Demanded by the deserializer |
| --- | --- |
| a non-nullable reference constructor parameter | yes |
| a nullable reference constructor parameter | no |
| a constructor parameter with a default value | no |
| a value type | no. See `HRDV003` |
| a settable property | no. Its initializer is invisible to reflection |
| a `[ResponseOnly]` member | no. The server owns the value |
| a get-only member with no constructor parameter | no |
| a member declared `required` or `[JsonRequired]` | yes |

A sent `null` is not an absence. It satisfies the deserializer and `[Required]` refuses it.

The rule above is installed by the reflection-based deserializer. A source-generated resolver
carries the same answer from its own generator, decided at build time.

A body parameter the handler declared non-nullable is checked by the generated binder. A literal
`null` body, and an empty body, both answer `required` against the body parameter name. A handler
declaring `Quote?` receives the null.

## Constraints on handler parameters

A constraint on a query, header or path parameter is compiled into the handler's parameters
validator.

```csharp
[BasePath("/constraints")]
public class ConstraintsController {

    [Get("/precision")]
    public int Precision([FromQueryString] [Range(Min = 2, Max = 8)] int precision) => precision;

    [Get("/region")]
    public string Region([FromHeader("X-Region")] [StringLength(2, 2)] string region) => region;

    [Get("/page/{count:int}")]
    public int Page([Range(Min = 1, Max = 100)] int count) => count;

    [Get("/tagged")]
    public string Tagged([FromQueryString] [Required] [Pattern("^[a-z]+$")] string? tag) => tag!;
}
```

A constraint attribute is not a binding attribute. The generator treats an unrecognised parameter
attribute as a custom binder. It recognises both constraint vocabularies, so a constrained parameter
still binds from its declared source.

A route constraint and a value constraint answer different questions. `/constraints/page/abc`
matches no route and answers 404. `/constraints/page/0` matches the route and answers 400.

A value that does not parse is a failure rather than an absence. `?limit=abc` answers 400 with code
`invalid` and the message `limit is not a valid Int32`. An omitted optional parameter still
succeeds.

## Nested models

A generated validator descends into a member only where `[ValidateNested]` says to.

```csharp
public class RegistrationModel {
    [Required]
    [StringLength(Min = 3, Max = 20)]
    public string? Name { get; set; }

    [ValidateNested]
    public AddressModel? Address { get; set; }
}

public sealed class AddressModel {
    [Required]
    public string? City { get; set; }

    [StringLength(2, 2)]
    public string? Country { get; set; }
}
```

`[ValidateNested]` covers an object, each element of a collection, and each value of a dictionary.
Omitting it stops every constraint on the child type, and the build reports `HRDV004`. An absent
optional nested member is not a failure.

## Contract-first constraints

An OpenAPI document or a Smithy model declares the same constraints. The build task writes them onto
the emitted members as the attributes above. The validation generator then reads them out of the
compilation. No part of the generator is contract-aware.

| OpenAPI facet | Attribute |
| --- | --- |
| `required` | `[Required]` |
| `minLength`, `maxLength` on a string | `[StringLength(Min =, Max =)]` |
| `minimum`, `maximum` on a number | `[Range(Min =, Max =)]` |
| `exclusiveMinimum`, `exclusiveMaximum` | `[Range(ExclusiveMin = true, ExclusiveMax = true)]` |
| `pattern` on a string | `[Pattern(typeof(PetstorePatterns), nameof(PetstorePatterns.P_c37a8736))]` |
| `minItems`, `maxItems` | `[ItemCount(Min =, Max =)]` |
| `enum` on a string | `[AllowedValues("a", "b")]` |

The task drops a facet the C# type cannot carry. A `minimum` on a string emits nothing. So does an
`enum` on a member that fell back to `JsonElement`, and a `minLength` on an integer. The comparison
the validator would emit does not compile.

Two cases suppress `required`. A non-nullable value type suppresses it, for the reason `HRDV003`
gives. A path parameter suppresses it, because the router already refused a request with an absent
segment.

A pattern becomes a `[GeneratedRegex]` member on a class named after the contract file, such as
`PetstorePatterns`. The attribute points at that member. Identical patterns collapse onto one
member. A pattern that .NET's engine refuses is reported against the contract and emits no
attribute.

The task maps no `multipleOf`, `uniqueItems` or `not`. A contract that declares one of those gets
warning `HOAT024`. A Smithy model gets `HSMT024`. The message names the keyword and where it sits.

A Smithy model declares the same constraints as traits. The parser reads each trait into the facet
above.

| Smithy trait | Facet |
| --- | --- |
| `@required` | `required` |
| `@length` on a string | `minLength`, `maxLength` |
| `@length` on a list, set or map | `minItems`, `maxItems` |
| `@range` | `minimum`, `maximum` |
| `@pattern` | `pattern` |

A trait may sit on the member or on the shape it targets. The member wins where both declare one.

For a constrained operation the task emits a public partial interface, named for the operation's
method. `GetPet` gets `IGetPetParameters`. The interface carries one get-only property per
parameter, plus a `body` member for a constrained body. The generated `Parameters` class implements
it, and the handler carries `[Validate<IGetPetParameters>]`. An operation the contract constrains
nothing about gets no interface and no filter.

## What the document publishes

An operation with a generated validator publishes a 400. Its schema is
`#/components/schemas/RequestValidationError`, and the build writes that schema into `components`.
An operation that declares its own 400 keeps it.

Parameter and property constraints are published as facets on the parameter's or the property's
schema.

```json
{
  "name": "precision",
  "in": "query",
  "schema": { "type": "integer", "minimum": 2, "maximum": 8 }
}
```

Facets are not written beside a `$ref`, because OpenAPI 3.0 readers ignore a `$ref`'s siblings.
Exclusive bounds use the JSON Schema 2020-12 spelling, so the bound is the number under
`exclusiveMinimum`. `[Pattern]` in its `typeof` form publishes no `pattern`, because the expression
lives in an attribute on another type.

## Changing the status code

A handler declares the status a validation failure answers with `[Throws<RequestValidationError>]`.

```csharp
using Hardened.Requests.Runtime.Validation;
using Hardened.Web.Runtime.Responses;

[Post("/registration/declared-422")]
[Throws<RequestValidationError>(422)]
public string Register(RegistrationModel model) => model.Name ?? "";
```

The declaration serves both halves. It puts the 422 in the published document, and it sets the
status the runtime answers with. The filter's refusal, a handler's own `ValidationException`, and a
body the deserializer could not read all answer 422. The document then carries the 422 alone, with
no 400 beside it.

The match is by full name. An application's own type called `RequestValidationError` sets nothing.

Contract-first, an operation that declares a 422 error response reaches the same result.

## Changing the body

`ExceptionToModelConverter` is registered with `RegistrationType.Try`, so an application replaces it
by registering its own.

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;

[SingletonService(Using = RegistrationType.Replace)]
public class ProblemDetailConverter : IExceptionToModelConverter {
    public (int, object) ConvertExceptionToModel(IExecutionContext context, Exception exp) {
        // return the status and the body for every failure, validation included
    }
}
```

Use `Replace` rather than `Add`. `Add` leaves the framework's descriptor registered, and anything
enumerating the service would still construct it.

## Raising a failure from handler code

A handler throws `ValidationException` for a rule the attributes cannot express. The response is the
same envelope at the same status.

```csharp
using ValidationModules;
using ValidationException = Hardened.Requests.Runtime.Validation.ValidationException;

[Post("/orders")]
public string Place(OrderModel model) {
    if (!_catalogue.Stocks(model.Sku)) {
        throw new ValidationException(ValidationResult.FromErrors([
            new ValidationError("model.sku", "unknown_sku", $"{model.Sku} is not stocked.")
        ]));
    }

    return model.Sku;
}
```

`ValidationModules.ValidationException` reaches the same response, so a handler calling that
library's own `ValidateAndThrow` needs no adaptation.

This envelope reports a field the caller got wrong. A refusal that is not about a field declares its
own status instead. The built-in response types are plain records, so a handler throws one through
`AsException()`. `[Throws<T>]` on the method puts that status in the published document.

```csharp
[Post("/todos")]
[Throws<Conflict>]
public Todo Create(NewTodo body) {
    if (_store.Has(body.Title)) {
        throw new Conflict($"A todo titled '{body.Title}' already exists.").AsException();
    }

    return _store.Add(body);
}
```

`[Throws<Conflict>]` states no status, because `Conflict` carries `[HttpStatus(409)]`. The full set
of response types is in [responses](/guide/responses).

## Adding a validator by hand

The filter resolves every `IValidatorFor<T>` registered for the validated type and runs them all
into one collector. A hand-written validator adds to the generated checks. It cannot replace them or
suppress them.

An `IAsyncValidatorFor<T>` runs after the structural checks, and only when they passed. It is
resolved per request, so it may be registered scoped.

The filter is built on the first request and kept. The build throws `InvalidOperationException` when
no validator is registered for the type. An empty set means the application was wired against
another entry point. The filter refuses to read it as an absence of work.

The filter also throws `InvalidOperationException` when the bound parameters are not the validated
type. Continuing would answer the request with nothing validated.

## Build properties

| Property | Values | Effect |
| --- | --- | --- |
| `ValidationModules_FieldNaming` | `PascalCase`, `AsDeclared`, `SnakeCase` | The spelling of a field in an error. The default is camelCase. |
| `ValidationModules_DataAnnotations` | `Ignore` | Stops compiling the DataAnnotations vocabulary. |
| `ValidationModules_PatternPolicy` | `Error`, `Warn`, `Allow` | How an inline `[Pattern("...")]` is treated. |

The pattern policy defaults to `Error` when the project sets `PublishAot` or `IsAotCompatible`, and
to `Allow` otherwise. An inline pattern makes the generator construct a `Regex`, which roots the
regex parser and interpreter in an AOT publish.

## Next

- [Parameter binding](/guide/parameter-binding)
- [The execution pipeline](/guide/execution-pipeline)
- [Generating from OpenAPI](/guide/openapi)
- [Diagnostics](/reference/diagnostics)
