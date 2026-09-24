# Validation

Constraint attributes on the members of a request body's type, or on a handler's parameters, are
checked after the request is bound and before the handler runs. `Hardened.Validation.SourceGenerator`
compiles the attributes from `ValidationModules.Constraints` into the check.

```csharp
using ValidationModules.Constraints;

namespace Todos;

public class NewList
{
    [Required]
    [StringLength(Min = 3, Max = 40)]
    public string? Name { get; set; }

    [Range(1, 50)]
    public int Capacity { get; set; }
}
```

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[BasePath("/lists")]
public class ListController
{
    [Post("/")]
    public NewList Create(NewList list) => list;
}
```

The handler declares no filter and registers nothing. A request that fails a constraint answers 400.
The body names each field that failed, with a code and a message.

```http
POST /todos/lists
Content-Type: application/json

{"name":"ab","capacity":0}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"list.name","code":"string_length","message":"name must be between 3 and 40 characters."},{"field":"list.capacity","code":"range","message":"capacity must be between 1 and 50."}]}
```

```http
POST /todos/lists
Content-Type: application/json

{"name":"Groceries","capacity":5}

HTTP/1.1 200 OK
Content-Type: application/json

{"name":"Groceries","capacity":5}
```

## Packages

`ValidationModules.Constraints` is in the `ValidationModules.Runtime` package, which
`Hardened.Web.Runtime` brings.

`Hardened.Validation.SourceGenerator` goes in the project that holds the constrained handlers. The
`hardened-web` template references it in `src/Todos`. The C# examples on this page are files in that
project. The library module's `[BasePath("/todos")]` puts their routes under `/todos`. A project that
sets package versions in its project file adds this reference:

```xml
<PackageReference Include="Hardened.Validation.SourceGenerator" Version="0.0.0-HARDENED-VERSION" />
```

The two generator packages give these results:

| The project references | Result |
|---|---|
| `Hardened.Validation.SourceGenerator` alone | The constraints are checked |
| Neither | No constraint is checked. The build reports warning `HRDV006` once for the project, naming one handler |
| `ValidationModules.SourceGenerator` alone | It generates validators and registers them. No handler runs them. The build reports `HRDV006` |
| Both | The build fails with `CS0111` and `CS0102` in the generated validators |

`Hardened.Validation.SourceGenerator` reads constraint attributes only. A ValidationModules rules
class, an `IValidationRulesFor<T>`, produces no check and no warning.

## Constraint attributes

The generator compiles these attributes:

| Attribute | Arguments | `code` | In the OpenAPI document |
|---|---|---|---|
| `[Required]` | `AllowEmptyStrings` | `required` | The member is listed in the schema's `required` |
| `[StringLength]` | `(min, max)`, or `Min` and `Max` | `string_length` | `minLength`, `maxLength` |
| `[Range]` | `(min, max)`, or `Min` and `Max`. `ExclusiveMin` and `ExclusiveMax` | `range` | `minimum`, `maximum`, or `exclusiveMinimum`, `exclusiveMaximum` |
| `[Pattern]` | `("regex")`, or `(typeof(T), nameof(T.Member))` for a `[GeneratedRegex]` method | `pattern` | `pattern`, for the string form only |
| `[ItemCount]` | `(min, max)`, or `Min` and `Max` | `array_bounds` | `minItems`, `maxItems` |
| `[AllowedValues]` | The permitted values | `enum` | `enum` |
| `[DeniedValues]` | The refused values | `enum` | Nothing |
| `[MultipleOf]` | The divisor | `multiple_of` | `multipleOf` |
| `[UniqueItems]` | None | `unique_items` | `uniqueItems` |
| `[EmailAddress]` | None | `email` | Nothing |
| `[Phone]` | None | `phone` | Nothing |
| `[Url]` | None | `url` | Nothing |
| `[CreditCard]` | None | `credit_card` | Nothing |
| `[Base64String]` | None | `base64` | Nothing |
| `[FileExtensions]` | `Extensions`, by default `png,jpg,jpeg,gif` | `file_extension` | Nothing |
| `[ValidateNested]` | Optionally a `Polymorphism`. See [Nested models](#nested-models) | The codes of the member type's constraints | Nothing |

`[Required]` fails on null, on an empty string and on a string of white space. With
`AllowEmptyStrings = true`, it fails on null only. Every other constraint passes a null value.

::: warning
A member without `[Required]` accepts null, whatever else constrains it. The template's
`record NewTodo([property: StringLength(1, 64)] string Title)` accepts `{"title":null}`.
`POST /todos` answers 201 with `{"id":3,"title":null,"done":false}`.
:::

When `[Required]` fails, the member's other constraints are not checked. Two constraints other than
`[Required]` that fail on one member are both reported.

`[Range]` takes `int`, `long`, `double` or `string` bounds. A `decimal` bound is written as a string,
such as `[Range("0.5", "30")]`. The OpenAPI document publishes it as a number. An exclusive bound is
published under `exclusiveMinimum` or `exclusiveMaximum`, with the bound as its value. For
`[Range(0.0, 1.0, ExclusiveMax = true)]`, the document publishes `"minimum": 0, "exclusiveMaximum": 1`.

Every constraint takes `Code` and `Message`, which replace the error's code and message. `{field}`
in `Message` becomes the member's name.

On a model's member, `When` names a `bool` member of the same model. The constraint is checked only
when that member is true. `Unless` checks the constraint only when the member it names is false.

The OpenAPI document publishes the constraints on the model's schema. The first example's `NewList`
publishes this schema:

```json
"NewList": {
  "type": "object",
  "required": [
    "name",
    "capacity"
  ],
  "properties": {
    "name": {
      "type": [
        "string",
        "null"
      ],
      "minLength": 3,
      "maxLength": 40
    },
    "capacity": {
      "type": "integer",
      "format": "int32",
      "minimum": 1,
      "maximum": 50
    }
  }
}
```

`capacity` is in `required` because its type is `int`. [The OpenAPI document](/guide/openapi-document)
covers the rest of the schema.

## Constraints on parameters

A constraint on a path, query or header parameter is checked like one on a model's member.

```csharp
using Hardened.Web.Runtime.Attributes;
using ValidationModules.Constraints;

namespace Todos;

[BasePath("/search")]
public class SearchController
{
    [Get("/")]
    public async Task<IReadOnlyList<Todo>> Search(
        ITodoStore store,
        [FromQueryString("q")] [StringLength(Min = 2)] string text,
        [FromQueryString] [Range(1, 100)] int? limit,
        [FromHeader("X-Region")] [StringLength(2, 2)] string? region
    )
    {
        var todos = await store.All();

        return todos
            .Where(todo => todo.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Take(limit ?? 10)
            .ToList();
    }
}
```

```http
GET /todos/search?q=a&limit=0
X-Region: USA

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"q","code":"string_length","message":"q must be at least 2 characters."},{"field":"limit","code":"range","message":"limit must be between 1 and 100."},{"field":"X-Region","code":"string_length","message":"X-Region must be between 2 and 2 characters."}]}
```

A value that does not convert to the parameter's type answers 400 with code `invalid`:

```http
GET /todos/search?q=read&limit=abc

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"limit","code":"invalid","message":"limit is not a valid Int32."}]}
```

A non-nullable query or header parameter that the request leaves out answers with code `required`.
An omitted nullable parameter is not checked.

A route constraint is checked before any value constraint. With a route `/page/{count:int}` and
`[Range(Min = 1, Max = 100)] int count`, `/page/abc` answers 404 and `/page/0` answers 400.
[Routing](/guide/routing) covers route constraints.

`When` or `Unless` on a parameter's constraint fails the build with `HRDV005`.

The OpenAPI document publishes a parameter's constraints on the parameter's schema.

## Nested models

`[ValidateNested]` on a member checks the constraints of the member's type on an object, on each
element of a collection, or on each value of a dictionary. This `NewList` replaces the one in the
first example:

```csharp
using ValidationModules.Constraints;

namespace Todos;

public sealed class ListItem
{
    [Required]
    [StringLength(1, 64)]
    public string? Title { get; set; }
}

public class NewList
{
    [Required]
    [StringLength(Min = 3, Max = 40)]
    public string? Name { get; set; }

    [Range(1, 50)]
    public int Capacity { get; set; }

    [ItemCount(Max = 3)]
    [ValidateNested]
    public List<ListItem>? Items { get; set; }
}
```

```http
POST /todos/lists
Content-Type: application/json

{"name":"Groceries","capacity":5,"items":[{"title":"Milk"},{"title":""}]}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"list.items[1].title","code":"required","message":"title is required."}]}
```

Without `[ValidateNested]`, the member type's constraints are not checked. The build reports warning
`HRDV004` for such a member when its parent declares a constraint of its own and the member's type is
declared in the same project. A member that is null is not checked.

The build reports warning `VM1503` for `[ValidateNested]` on a member whose type is not sealed. Seal
the type, or write `[ValidateNested(Polymorphism.DeclaredOnly)]`, which checks the declared type's
constraints only. `Polymorphism` is in `ValidationModules.Constraints`.

## The failure response

The body has these members:

| Member | Value |
|---|---|
| `type` | `ValidationError` |
| `message` | `One or more validation errors occurred.` |
| `errors` | An entry for each failed constraint, with `field`, `code` and `message` |

`message` differs only for a `ValidationModules.ValidationException`, which
[Throw from the handler](#throw-from-the-handler) covers. `errors` lists every failed constraint. A
member whose `[Required]` fails reports that failure alone.

Each of these refusals answers with the same body:

| The request | `field` | `code` | `message` |
|---|---|---|---|
| Fails a constraint | The field. See [Field names](#field-names) | The constraint's code | The constraint's message |
| Has a path or query value that does not convert, or a member of a query-string or form model that does not | The parameter's or the field's name | `invalid` | Such as `limit is not a valid Int32.` |
| Leaves out a non-nullable query or header value, or a required member of a query-string or form model | The parameter's or the field's name | `required` | Such as `q is required.` |
| Has a body that is not JSON | The body parameter's name | `invalid` | The parser's message, such as `Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.` |
| Has a body member of the wrong JSON type | The member's field | `invalid` | Such as `The JSON value could not be converted to Todos.NewTodo.` |
| Has an empty body, or the body `null` | The body parameter's name | `required` | Such as `request is required.` |

The template's `POST /todos` takes its body as `NewTodo request`:

```http
POST /todos
Content-Type: application/json

{"title":

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"request","code":"invalid","message":"Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed."}]}
```

A handler whose body parameter is nullable, such as `NewTodo? request`, receives null for the body
`null`. An empty body still answers `required`.

A value an enum does not declare is refused as `invalid` when it is read, before any constraint runs.

For an operation that validates, the OpenAPI document lists a 400 with the `RequestValidationError`
schema. [The execution pipeline](/guide/execution-pipeline) shows where the check runs among the
filters.

## Field names

`field` names the value that failed:

| Where the value is | `field` | Example |
|---|---|---|
| A body member | The handler's body parameter name, a dot, then the path to the member | `list.name` for `Create(NewList list)` |
| A collection element | The path with the element's index | `list.items[1].title` |
| A dictionary value | The path with the key | `body.map[k1].title` |
| A path, query or header value | The name the request uses | `q` for `[FromQueryString("q")] string text`, and `X-Region` for `[FromHeader("X-Region")]` |
| A member of a `[FromQueryString]` or `[FromForm]` model that fails a constraint | The model parameter's name, a dot, then the member | `filter.limit` |
| A member of such a model that is missing or does not convert | The field's name alone | `limit` |

A member's name has its first letter in lower case. `DueDate` reports as `dueDate`. `message` spells
the member's name the same way, as in `name must be between 3 and 40 characters.`
`ValidationModules_FieldNaming` changes the spelling of a member's name.
[Build properties](#build-properties) lists its values.

A model bound from the query string or a form is checked after it is bound, like a body model.
[Forms and files](/guide/forms) covers binding a model from fields.

```csharp
using Hardened.Web.Runtime.Attributes;
using ValidationModules.Constraints;

namespace Todos;

public record TodoFilter(
    [property: Range(1, 100)] int Limit,
    [property: StringLength(Min = 2)] string Text
);

[BasePath("/filtered")]
public class TodoFilterController
{
    [Get("/")]
    public string Find([FromQueryString] TodoFilter filter) => $"{filter.Limit} {filter.Text}";
}
```

```http
GET /todos/filtered?limit=0&text=a

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"filter.limit","code":"range","message":"limit must be between 1 and 100."},{"field":"filter.text","code":"string_length","message":"text must be at least 2 characters."}]}
```

A member of such a model that is missing or does not convert is refused while the model is bound,
before any constraint runs:

```http
GET /todos/filtered?text=ab

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"limit","code":"required","message":"limit is required."}]}
```

Only the first missing member is reported.

## Change the status to 422

`[Throws<RequestValidationError>(422)]` on a handler answers every validation refusal of that handler
with 422: a failed constraint, a body that cannot be read, and a `ValidationException` the handler
throws. The OpenAPI document then lists the 422 with the `RequestValidationError` schema, and no 400.

`RequestValidationError` is in `Hardened.Requests.Runtime.Validation`. `[Throws<T>]` is in
`Hardened.Web.Runtime.Responses`. [Declared responses](/guide/responses) covers it.

```csharp
using Hardened.Requests.Runtime.Validation;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Todos;

[BasePath("/imports")]
public class ImportController
{
    [Post("/")]
    [Throws<RequestValidationError>(422)]
    public Todo Import(NewTodo request) => new Todo(0, request.Title, false);
}
```

```http
POST /todos/imports
Content-Type: application/json

{"title":""}

HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"request.title","code":"string_length","message":"title must be between 1 and 64 characters."}]}
```

## Replace the failure body

The registered `IExceptionToModelConverter` writes the body of every refusal that comes from an
exception, a validation failure included. The interface is in `Hardened.Requests.Abstract.Errors`.
The default converter is `ExceptionToModelConverter`, in `Hardened.Requests.Runtime.Errors`.

A class that implements the interface and carries
`[SingletonService(Using = RegistrationType.Replace)]` replaces the default converter.
`RegistrationType` is in `DependencyModules.Runtime.Attributes`. The replacement can call the default
converter for the status and the body it would have sent.

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Validation;

namespace Todos;

public record ValidationProblem(
    string Title,
    int Status,
    IReadOnlyList<RequestValidationFieldError> Errors
);

[SingletonService(Using = RegistrationType.Replace)]
public class ValidationProblemConverter : IExceptionToModelConverter
{
    private readonly ExceptionToModelConverter _default = new();

    public (int, object) ConvertExceptionToModel(IExecutionContext context, Exception exp)
    {
        var (status, model) = _default.ConvertExceptionToModel(context, exp);

        if (model is RequestValidationError validation)
        {
            return (status, new ValidationProblem("The request is invalid.", status, validation.Errors));
        }

        return (status, model);
    }
}
```

A request that fails a constraint then answers with the new body:

```http
GET /todos/0

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"title":"The request is invalid.","status":400,"errors":[{"field":"id","code":"range","message":"id must be at least 1."}]}
```

A response the handler returns, such as a `NotFound`, does not pass through the converter. The
OpenAPI document still describes the 400 with the `RequestValidationError` schema.

## Rules the attributes cannot state

### Throw from the handler

A handler throws `ValidationException`, from `Hardened.Requests.Runtime.Validation`, with a
`ValidationModules.ValidationResult`. The request answers with the same body and status as a failed
constraint. `ValidationModules` also declares a `ValidationException`. The example names Hardened's
through an alias.

```csharp
using Hardened.Web.Runtime.Attributes;
using ValidationModules;
using ValidationException = Hardened.Requests.Runtime.Validation.ValidationException;

namespace Todos;

[BasePath("/drafts")]
public class DraftController
{
    [Post("/")]
    public Todo Create(NewTodo request)
    {
        if (string.Equals(request.Title, "inbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                ValidationResult.FromErrors([
                    new ValidationError("request.title", "reserved", "inbox is a reserved title."),
                ])
            );
        }

        return new Todo(0, request.Title, false);
    }
}
```

```http
POST /todos/drafts
Content-Type: application/json

{"title":"Inbox"}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"request.title","code":"reserved","message":"inbox is a reserved title."}]}
```

`ValidationModules.ValidationException`, which `ValidateAndThrow` throws, answers the same way. The
body's `message` is then the exception's own message, such as
`Validation failed: request.title reserved.`

### Register a validator

A class that implements `IValidatorFor<T>` for a body type, registered as a service, runs beside the
generated check for `T`. Its errors join the same list. The context it receives is positioned at the
body parameter. A field it reports as `"title"` appears as `request.title`.

`IValidatorFor<T>`, `ValidationContext`, `ValidationFlow` and `ValidationSeverity` are in
`ValidationModules`.

```csharp
using DependencyModules.Runtime.Attributes;
using ValidationModules;

namespace Todos;

[SingletonService]
public class NoShoutingValidator : IValidatorFor<NewTodo>
{
    public ValidationFlow Validate(ref ValidationContext context, NewTodo value)
    {
        if (IsValid(value))
        {
            return ValidationFlow.Continue;
        }

        return context.Report(
            "title",
            "shouting",
            "title must not be all capitals.",
            ValidationSeverity.Error
        );
    }

    public bool IsValid(NewTodo value) =>
        value.Title is not { Length: > 1 } title || title != title.ToUpperInvariant();
}
```

With the validator registered, the template's `POST /todos` answers:

```http
POST /todos
Content-Type: application/json

{"title":"URGENT"}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"request.title","code":"shouting","message":"title must not be all capitals."}]}
```

## DataAnnotations attributes

The generator also compiles the `System.ComponentModel.DataAnnotations` attributes. They report the
same codes:

| Attribute | `code` |
|---|---|
| `[Required]` | `required` |
| `[StringLength]` | `string_length` |
| `[Range]` | `range` |
| `[RegularExpression]` | `pattern` |
| `[EmailAddress]` | `email` |

The OpenAPI document does not publish them. `ValidationModules_DataAnnotations` set to `Ignore` stops
the generator compiling them. [Build properties](#build-properties) lists it.

A `NoteController` under `[BasePath("/notes")]` has a `[Post("/")]` handler that takes
`NewNote note`:

```csharp
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Todos;

public class NewNote
{
    [DataAnnotations.Required]
    [DataAnnotations.StringLength(200, MinimumLength = 3)]
    public string? Text { get; set; }

    [DataAnnotations.EmailAddress]
    public string? Author { get; set; }
}
```

```http
POST /todos/notes
Content-Type: application/json

{"text":"hi","author":"nobody"}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"note.text","code":"string_length","message":"text must be between 3 and 200 characters."},{"field":"note.author","code":"email","message":"author is not a valid email address."}]}
```

## Build properties

The generator reads these build properties:

| Property | Values | Default | Effect |
|---|---|---|---|
| `ValidationModules_FieldNaming` | `SnakeCase`, `PascalCase`, `AsDeclared` | First letter in lower case | The spelling of a member's name in `field` and `message` |
| `ValidationModules_DataAnnotations` | `Ignore` | The DataAnnotations attributes are compiled | `Ignore` stops compiling them |
| `ValidationModules_PatternPolicy` | `Error`, `Warn`, `Allow` | `Error` when `PublishAot` or `IsAotCompatible` is `true`, `Allow` otherwise | How the build reports `[Pattern("...")]` with an inline expression: error or warning `VM1301`, or nothing |

This excerpt of `src/Todos/Todos.csproj` sets `ValidationModules_FieldNaming`:

```xml
<PropertyGroup>
  <ValidationModules_FieldNaming>SnakeCase</ValidationModules_FieldNaming>
</PropertyGroup>
```

With `SnakeCase`, a `DueDate` member of a body parameter named `reminder` reports as
`reminder.due_date`, with the message `due_date is required.` `PascalCase` and `AsDeclared` both
write the member's name as it is declared, `DueDate`. The body parameter's name, and the names of
path, query and header values, do not change.

`[Pattern(typeof(T), nameof(T.Member))]` is not reported under any policy.

## Diagnostics

The build reports these diagnostics:

| Code | Severity | Reported when |
|---|---|---|
| `HRDV002` | Error | Two validators claim one generated file. The message asks for a defect report |
| `HRDV003` | Warning | `[Required]` is on a non-nullable value type, such as `int`, which is never null. ValidationModules reports `VM1201` beside it |
| `HRDV004` | Warning | A member's type declares constraints and the member has no `[ValidateNested]` |
| `HRDV005` | Error | A parameter's constraint sets `When` or `Unless` |
| `HRDV006` | Warning | The project declares constraints on a handler and does not reference `Hardened.Validation.SourceGenerator` |
| `VM1301` | Error or warning, by `ValidationModules_PatternPolicy` | `[Pattern("...")]` has an inline expression |
| `VM1503` | Warning | `[ValidateNested]` is on a member whose type is not sealed |

`[Required]` never fails on an `int` member, which reads as `0` when it is omitted.
[JSON serialization](/guide/json) covers the `required` modifier and `[JsonRequired]`, which make the
value's absence a refusal.

## Limits

An `IAsyncValidatorFor<T>` registered for a body type is not run.

These constraints are checked and are not published in the OpenAPI document: `[DeniedValues]`,
`[EmailAddress]`, `[Phone]`, `[Url]`, `[CreditCard]`, `[Base64String]`, `[FileExtensions]`,
`[Pattern]` with a `[GeneratedRegex]`, and every DataAnnotations attribute.

## Next

- [Parameter binding](/guide/parameter-binding): where each parameter's value comes from
- [Declared responses](/guide/responses): `[Throws<T>]` and the responses a handler returns
- [The execution pipeline](/guide/execution-pipeline): where the check runs among the filters
- [JSON serialization](/guide/json): required members and absent values
- [Diagnostics](/reference/diagnostics): every diagnostic code
