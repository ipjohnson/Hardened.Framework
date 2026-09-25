# Validation

Constraint attributes on the members of a request body's type, or on a handler's parameters, are
checked after the request is bound and before the handler runs. `Hardened.Validation.SourceGenerator`
compiles the attributes into that check.

The body type goes in `src/Todos/NewList.cs`:

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

The handler goes in `src/Todos/ListController.cs`:

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

The examples on this page add files to `src/Todos` in an application made with
`dotnet new hardened-web -n Todos`. Their routes answer under `/todos`, which the template's library
module sets with `[BasePath("/todos")]`.

A request that fails a constraint answers 400. The body names each field that failed, with a code
and a message:

```http
POST /todos/lists
Content-Type: application/json

{"name":"ab","capacity":0}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"list.name","code":"string_length","message":"name must be between 3 and 40 characters."},{"field":"list.capacity","code":"range","message":"capacity must be between 1 and 50."}]}
```

A body that passes reaches the handler:

```http
POST /todos/lists
Content-Type: application/json

{"name":"Groceries","capacity":5}

HTTP/1.1 200 OK
Content-Type: application/json

{"name":"Groceries","capacity":5}
```

The handler declares no filter and registers nothing.

## Packages

The constraint attributes are in `ValidationModules.Constraints`. That namespace is in the
`ValidationModules.Runtime` package, which `Hardened.Web.Runtime` brings.

`Hardened.Validation.SourceGenerator` goes in the project that holds the constrained handlers. The
`hardened-web` template references it in `src/Todos`. A project that sets versions in its project
file references it with this line:

```xml
<PackageReference Include="Hardened.Validation.SourceGenerator" Version="0.0.0-HARDENED-VERSION" />
```

The generator reads constraint attributes only. A ValidationModules rules class, an
`IValidationRulesFor<T>`, produces no check and no warning.

Other generator references have these results:

| Validation generator in the project | Result |
|---|---|
| None | No constraint is checked. The build reports warning `HRDV006` once for the project, naming one handler |
| `ValidationModules.SourceGenerator` in place of `Hardened.Validation.SourceGenerator` | It generates validators and registers them. No handler runs them. The build reports `HRDV006` |
| Both | The build fails with `CS0111` and `CS0102` in the generated validators |

## Constraint attributes

Each attribute reports the code in the table when it fails. The last column is what the OpenAPI
document publishes for it.

| Attribute | Arguments | Code | In the OpenAPI document |
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
| `[ValidateNested]` | Optionally a `Polymorphism`, described in [Nested models](#nested-models) | The codes of the member type's constraints | Nothing |

`[Required]` fails on null, on an empty string and on a string of white space. With
`AllowEmptyStrings = true`, it fails on null only. When `[Required]` fails, the member's other
constraints are not checked. When two other constraints fail on one member, both are reported.

Every constraint except `[Required]` passes a null value.

::: warning
A member without `[Required]` accepts null, whatever else constrains it. The template's
`record NewTodo([property: StringLength(1, 64)] string Title)` accepts `{"title":null}`.
`POST /todos` answers 201 with `{"id":3,"title":null,"done":false}`.
:::

`[Range]` takes `int`, `long`, `double` or `string` bounds. A `decimal` bound is written as a
string, such as `[Range("0.5", "30")]`. The OpenAPI document publishes it as a number. An exclusive
bound is published under `exclusiveMinimum` or `exclusiveMaximum`, with the bound as its value.
The OpenAPI document publishes `[Range(0.0, 1.0, ExclusiveMax = true)]` as
`"minimum": 0, "exclusiveMaximum": 1`.

Every constraint takes `Code` and `Message`, which replace the error's code and message. `{field}`
in `Message` becomes the member's name.

On a model's member, `When` names a `bool` member of the same model. The constraint is then checked
only when that member is true. `Unless` checks the constraint only when the member it names is
false.

The OpenAPI document publishes the constraints on the model's schema. This is the schema for the
first example's `NewList`:

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

`capacity` is in `required` because its type is `int`.
[The OpenAPI document](/guide/openapi-document) covers the rest of the schema.

### A timeout on a pattern

`[Pattern(typeof(T), nameof(T.Member))]` uses the `[GeneratedRegex]` method you declare, with the
timeout you give it. Without a timeout, a pattern that backtracks catastrophically, such as
`^(a+)+$`, runs on the request's thread for as long as a crafted value makes it. Give the method a
timeout:

```csharp
using System.Text.RegularExpressions;

namespace Todos;

public static partial class TodoPatterns
{
    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    public static partial Regex Slug();
}
```

A match that runs past its timeout throws `RegexMatchTimeoutException`, and the request answers 500.
A `pattern` from an OpenAPI document or a Smithy model gets a timeout of 2,000 milliseconds. That is
also the default of .NET's `RegularExpressionAttribute`.

## Constraints on parameters

A constraint on a path, query or header parameter is checked like one on a model's member. The
handler in `src/Todos/SearchController.cs` constrains three parameters:

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

This request fails all three:

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

A non-nullable query or header parameter that the request leaves out answers `required`. An
omitted nullable parameter is not checked.

The OpenAPI document publishes a parameter's constraints on the parameter's schema.

A route constraint is checked before any value constraint. With a route `/page/{count:int}` and
`[Range(Min = 1, Max = 100)] int count`, `/page/abc` answers 404. `/page/0` answers 400.
[Routing](/guide/routing) covers route constraints.

`When` or `Unless` on a parameter's constraint fails the build with `HRDV005`.

## Nested models

`[ValidateNested]` on a member checks the constraints of the member's type. It checks an object,
each element of a collection, or each value of a dictionary. This `src/Todos/NewList.cs` replaces
the first one and adds a list of items:

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

The second item fails `[Required]`:

```http
POST /todos/lists
Content-Type: application/json

{"name":"Groceries","capacity":5,"items":[{"title":"Milk"},{"title":""}]}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"list.items[1].title","code":"required","message":"title is required."}]}
```

Without `[ValidateNested]`, the member type's constraints are not checked. The build reports
warning `HRDV004` for such a member when its parent declares a constraint of its own and the
member's type is declared in the same project. A member that is null is not checked.

`[ValidateNested]` on a member whose type is not sealed reports warning `VM1503`. Seal the type, or
write `[ValidateNested(Polymorphism.DeclaredOnly)]`, which checks the declared type's constraints
only. `Polymorphism` is in `ValidationModules.Constraints`.

## The failure response

The body of a validation refusal has three members:

| Member | Value |
|---|---|
| `type` | `ValidationError` |
| `message` | `One or more validation errors occurred.` |
| `errors` | The failures. Each entry has `field`, `code` and `message` |

[Rules the attributes cannot state](#rules-the-attributes-cannot-state) describes the one exception
to that `message`.

`errors` lists every failed constraint, not only the first. The constraints after a failed
`[Required]` are the exception. A handler under
`[ValidationMode(ValidationStopMode.StopOnFirstError)]` lists only the first, as
[Stop at the first failure](#stop-at-the-first-failure) describes.

The same body answers each of these refusals:

| The request | `field` | `code` | `message` |
|---|---|---|---|
| Fails a constraint | The field, named as in [Field names](#field-names) | The constraint's code | The constraint's message |
| Has a path or query value that does not convert, or a member of a query-string or form model that does not | The parameter's or the field's name | `invalid` | `limit is not a valid Int32.` |
| Leaves out a non-nullable query or header value, or a required member of a query-string or form model | The parameter's or the field's name | `required` | `q is required.` |
| Has a body that is not JSON | The body parameter's name | `invalid` | The parser's message, such as `Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.` |
| Has a body member of the wrong JSON type | The member's field | `invalid` | `The JSON value could not be converted to Todos.NewTodo.` |
| Has an empty body, or the body `null` | The body parameter's name | `required` | `request is required.` |

A handler whose body parameter is nullable, such as `NewTodo? request`, receives null for the body
`null`. An empty body still answers `required`.

A value an enum does not declare is refused as `invalid` when it is read, before any constraint
runs.

The body parameter of the template's `POST /todos` is `NewTodo request`. A body that is not JSON
answers with the parser's message:

```http
POST /todos
Content-Type: application/json

{"title":

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"request","code":"invalid","message":"Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed."}]}
```

The OpenAPI document lists a 400 with the `RequestValidationError` schema for an operation that
validates. [The execution pipeline](/guide/execution-pipeline) shows where the check runs among the
filters.

## Field names

Each entry's `field` depends on where the value is:

| Where the value is | `field` | Example |
|---|---|---|
| A body member | The handler's body parameter name, a dot, then the path to the member | `list.name` for `Create(NewList list)` |
| A collection element | The path with the element's index | `list.items[1].title` |
| A dictionary value | The path with the key | `body.map[k1].title` |
| A path, query or header value | The name the request uses | `q` for `[FromQueryString("q")] string text`, and `X-Region` for `[FromHeader("X-Region")]` |
| A member of a `[FromQueryString]` or `[FromForm]` model that fails a constraint | The model parameter's name, a dot, then the member | `filter.limit` |
| A member of such a model that is missing or does not convert | The field's name alone | `limit` |

A member's name is written with its first letter in lower case: `DueDate` reports as `dueDate`.
The member's name in `message` is spelled the same way, as in
`name must be between 3 and 40 characters.` `ValidationModules_FieldNaming` changes the spelling,
as [Build properties](#build-properties) describes.

A model bound from the query string or a form is checked after it is bound, like a body model.
[Forms and files](/guide/forms) covers binding a model from fields. The handler in
`src/Todos/TodoFilterController.cs` binds one from the query string:

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

This request fails both constraints:

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

## Stop at the first failure

`[ValidationMode(ValidationStopMode.StopOnFirstError)]` on a handler method, or on its class,
answers a request that fails validation with its first failure only. `errors` then has one entry.
`ValidationStopMode.CollectAll` answers with every failure. A handler that declares no mode gets
`CollectAll`.

The class in `src/Todos/QuickListController.cs` declares `StopOnFirstError`. Its `CreateFull` method
declares `CollectAll`. `NewList` is the version from [Nested models](#nested-models):

```csharp
using Hardened.Requests.Runtime.Validation;
using Hardened.Web.Runtime.Attributes;
using ValidationModules;

namespace Todos;

[BasePath("/quick-lists")]
[ValidationMode(ValidationStopMode.StopOnFirstError)]
public class QuickListController
{
    [Post("/")]
    public NewList Create(NewList list) => list;

    [Post("/full")]
    [ValidationMode(ValidationStopMode.CollectAll)]
    public NewList CreateFull(NewList list) => list;
}
```

`Create` answers the first example's body with one entry:

```http
POST /todos/quick-lists
Content-Type: application/json

{"name":"ab","capacity":0}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"list.name","code":"string_length","message":"name must be between 3 and 40 characters."}]}
```

`CreateFull` answers the same body with both failures:

```http
POST /todos/quick-lists/full
Content-Type: application/json

{"name":"ab","capacity":0}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"list.name","code":"string_length","message":"name must be between 3 and 40 characters."},{"field":"list.capacity","code":"range","message":"capacity must be between 1 and 50."}]}
```

`ValidationModeAttribute` is in `Hardened.Requests.Runtime.Validation`, in the
`Hardened.Requests.Runtime` package. `ValidationStopMode` is ValidationModules' own enum, in the
namespace `ValidationModules` of `ValidationModules.Runtime`. `Hardened.Web.Runtime` brings that
package.

These declarations set the mode:

| Declared on | Sets the mode of |
|---|---|
| A handler method | The handler. The nearest declaration wins: a method's mode beats its class's |
| A handler class | Its handlers |
| A `[HardenedModule]` class | Every handler compiled in the module's project that declares no mode. Only `CollectAll` compiles there, as [Limits](#limits) describes |
| An operation in a contract, with `x-hardened-validation` in an OpenAPI document or `@validation` in a Smithy model | The operation. The contract's mode beats a `[ValidationMode]` on the module class and one on the implementation |

[Generating from OpenAPI](/guide/openapi) and [Generating from Smithy](/guide/smithy) cover
`x-hardened-validation` and `@validation`. The OpenAPI document publishes the mode as
`x-hardened-validation`. [The OpenAPI document](/guide/openapi-document) covers the published
extension.

The refusal keeps its body and its status: 400, or the 422 that
[Change the status to 422](#change-the-status-to-422) sets.

The mode belongs to the handler, not to the model. `NewList` gets every failure at `/todos/lists`
and only the first at `/todos/quick-lists`.

The first failure is the first in declaration order: the parameters in the order the handler
declares them, a model's members in the order its type declares them, and one member's constraints
in the order they are written. The checks after the first failure do not run: the member's other
constraints, the other members and parameters, and the elements of a collection. At
`/todos/quick-lists`, the body
`{"name":"Groceries","capacity":5,"items":[{"title":""},{"title":""}]}` reports
`list.items[0].title` alone.

A validator registered for the body type runs before the generated check for that type.
[Rules the attributes cannot state](#rules-the-attributes-cannot-state) shows one. Under
`StopOnFirstError`, when both fail, the refusal names the registered validator's failure alone. A
registered validator that reports two failures has only its first reported.

A `ValidationException` the handler throws is answered with every error it holds, in either mode.

## Skip validation

`[ValidateNever]` binds a parameter without checking its constraints. On a handler method, it
covers every parameter the handler binds. `ValidateNeverAttribute` is in
`Hardened.Requests.Runtime.Validation`.

A route that stores a draft takes the same `NewList` without its constraints. The handler is in
`src/Todos/DraftListController.cs`:

```csharp
using Hardened.Requests.Runtime.Validation;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[BasePath("/drafts")]
public class DraftListController
{
    [Post("/")]
    public NewList Save([ValidateNever] NewList list) => list;
}
```

`POST /todos/drafts` with the body `{"name":"ab","capacity":0}` answers 200 with the list as it was
sent. `POST /todos/quick-lists/full` refuses the same body with two failures.

A parameter beside a marked one is still checked. Binding still refuses a value that does not
convert to its type, and a body that does not deserialize. The OpenAPI document still publishes the
model's constraints, because the schema belongs to the type. A specification-first operation
ignores `[ValidateNever]` and checks what its contract declares.

## Change the status to 422

`[Throws<RequestValidationError>(422)]` on a handler answers every validation refusal of that
handler with 422: a failed constraint, a body that cannot be read, and a `ValidationException` the
handler throws. The handler in `src/Todos/ImportController.cs` declares it:

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

A failed constraint answers 422:

```http
POST /todos/imports
Content-Type: application/json

{"title":""}

HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"request.title","code":"string_length","message":"title must be between 1 and 64 characters."}]}
```

The OpenAPI document then lists the 422 with the `RequestValidationError` schema, and no 400.

`RequestValidationError` is in `Hardened.Requests.Runtime.Validation`. `[Throws<T>]` is in
`Hardened.Web.Runtime.Responses`. [Declared responses](/guide/responses) covers it.

## Change the body

The registered `IExceptionToModelConverter` writes the body of every refusal that comes from an
exception, a validation failure included. A class with
`[SingletonService(Using = RegistrationType.Replace)]` that implements it replaces the default.
`ExceptionToModelConverter` is the default. A replacement can call it for the status and the body
it would have sent.

The converter in `src/Todos/ValidationProblemConverter.cs` replaces the body of a validation
refusal:

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

`GET /todos/0` then answers with the new body:

```http
GET /todos/0

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"title":"The request is invalid.","status":400,"errors":[{"field":"id","code":"range","message":"id must be at least 1."}]}
```

A response the handler returns, such as a `NotFound`, does not pass through the converter. The
OpenAPI document still describes the 400 with the `RequestValidationError` schema.

The interface is in `Hardened.Requests.Abstract.Errors`. The default is in
`Hardened.Requests.Runtime.Errors`. `RegistrationType` is in `DependencyModules.Runtime.Attributes`.

## Rules the attributes cannot state

A handler can throw `ValidationException`, from `Hardened.Requests.Runtime.Validation`, with a
`ValidationModules.ValidationResult`. The request then answers with the same body and status as a
failed constraint. `ValidationModules` also declares a `ValidationException`. The example names
Hardened's through an alias.

The handler in `src/Todos/DraftController.cs` refuses the title `inbox`:

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

A draft titled `Inbox` is refused:

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

A class that implements `IValidatorFor<T>` for a body type, registered as a service, runs beside
the generated check for `T`. Its errors join the same list. The generated check for `T` still runs.
The registered validator runs first, so its errors come before the generated check's in `errors`.
The context it receives is positioned at the body parameter, so `"title"` is reported as
`request.title`.

The validator in `src/Todos/NoShoutingValidator.cs` checks `NewTodo`:

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

The template's `POST /todos` then answers:

```http
POST /todos
Content-Type: application/json

{"title":"URGENT"}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"request.title","code":"shouting","message":"title must not be all capitals."}]}
```

`IValidatorFor<T>`, `ValidationContext`, `ValidationFlow` and `ValidationSeverity` are in
`ValidationModules`.

## DataAnnotations attributes

The generator also compiles the `System.ComponentModel.DataAnnotations` attributes. They report the
same codes:

| Attribute | Code |
|---|---|
| `[Required]` | `required` |
| `[StringLength]` | `string_length` |
| `[Range]` | `range` |
| `[RegularExpression]` | `pattern` |
| `[EmailAddress]` | `email` |

A `NoteController` under `[BasePath("/notes")]` takes `NewNote note` in a `[Post("/")]` handler.
The model in `src/Todos/NewNote.cs` uses three of the attributes:

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

This body fails two of them:

```http
POST /todos/notes
Content-Type: application/json

{"text":"hi","author":"nobody"}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"note.text","code":"string_length","message":"text must be between 3 and 200 characters."},{"field":"note.author","code":"email","message":"author is not a valid email address."}]}
```

The DataAnnotations attributes do not reach the OpenAPI document. `ValidationModules_DataAnnotations`
set to `Ignore` stops the generator compiling them, as [Build properties](#build-properties)
describes.

## Build properties

The generator takes three build properties:

| Property | Values | Default | Effect |
|---|---|---|---|
| `ValidationModules_FieldNaming` | `SnakeCase`, `PascalCase`, `AsDeclared` | First letter in lower case | The spelling of a member's name in `field` and `message` |
| `ValidationModules_DataAnnotations` | `Ignore` | The DataAnnotations attributes are compiled | `Ignore` stops compiling them |
| `ValidationModules_PatternPolicy` | `Error`, `Warn`, `Allow` | `Error` when `PublishAot` or `IsAotCompatible` is `true`, `Allow` otherwise | How the build reports `[Pattern("...")]` with an inline expression: error or warning `VM1301`, or nothing |

This excerpt of `src/Todos/Todos.csproj` sets `SnakeCase`:

```xml
<PropertyGroup>
  <ValidationModules_FieldNaming>SnakeCase</ValidationModules_FieldNaming>
</PropertyGroup>
```

With `SnakeCase`, a `DueDate` member of a body parameter named `reminder` reports as
`reminder.due_date`, with the message `due_date is required.` `PascalCase` and `AsDeclared` both
write the member's name as it is declared: `DueDate`. The body parameter's name, and the names of
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

An omitted `int` member reads as `0`, so `[Required]` on it never fails.
[JSON serialization](/guide/json) covers the `required` modifier and `[JsonRequired]`, which make
the value's absence a refusal.

## Limits

An `IAsyncValidatorFor<T>` registered for a body type is not run.

The OpenAPI document does not publish `[DeniedValues]`, `[EmailAddress]`, `[Phone]`, `[Url]`,
`[CreditCard]`, `[Base64String]`, `[FileExtensions]`, `[Pattern]` with a `[GeneratedRegex]`, or any
DataAnnotations attribute. Each of them is still checked.

On a `[HardenedModule]` class, `[ValidationMode(ValidationStopMode.StopOnFirstError)]` fails the
build with `CS1503` in the generated `<Module>.Module.g.cs`:

```text
Argument 1: cannot convert from 'int' to 'ValidationModules.ValidationStopMode'
```

It fails on the template's library module and on its application module. It also fails when
written as `(ValidationStopMode)1` or as `stopMode: ValidationStopMode.StopOnFirstError`.

`[ValidationMode(ValidationStopMode.CollectAll)]` compiles on a module class. It is the default, so
it changes no refusal.

## Next

- [Parameter binding](/guide/parameter-binding): where each parameter's value comes from
- [Declared responses](/guide/responses): `[Throws<T>]` and the responses a handler returns
- [The execution pipeline](/guide/execution-pipeline): where the check runs among the filters
- [JSON serialization](/guide/json): required members and absent values
- [Diagnostics](/reference/diagnostics): every diagnostic code
