# Parameter binding

The build writes a binder for each handler. The binder reads each parameter from one source, chosen
at build time from the parameter's binding attribute, its name and its type.

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public record SearchResult(string Owner, string Tenant, string? Title, int Page, int Matches);

[BasePath("/search")]
public class SearchController
{
    [Get("/{owner}")]
    public async Task<SearchResult> Find(
        ITodoStore store,
        string owner,
        [FromQueryString] string? title,
        [FromHeader("X-Tenant")] string tenant,
        [FromQueryString] int page = 1
    )
    {
        var todos = await store.All();

        var matches = todos.Count(todo =>
            title is null || todo.Title.Contains(title, StringComparison.OrdinalIgnoreCase)
        );

        return new SearchResult(owner, tenant, title, page, matches);
    }
}
```

In the example, `owner` binds from the path token of the same name. `title` and `page` bind from the
query string. `tenant` binds from the `X-Tenant` header. `store` comes from the container.

```http
GET /todos/search/ada?title=read&page=2
X-Tenant: acme

HTTP/1.1 200 OK
Content-Type: application/json

{"owner":"ada","tenant":"acme","title":"read","page":2,"matches":1}
```

A missing value answers 400 with code `required` when the parameter is not nullable and has no
default:

```http
GET /todos/search/ada?title=read

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"X-Tenant","code":"required","message":"X-Tenant is required."}]}
```

A value that does not convert to the parameter's type answers 400 with code `invalid`:

```http
GET /todos/search/ada?page=two
X-Tenant: acme

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"page","code":"invalid","message":"page is not a valid Int32."}]}
```

## Binding attributes

A binding attribute sets the source of the parameter it is on:

| Attribute | Binds the parameter from | Namespace |
|---|---|---|
| `[FromQueryString]`, `[FromQueryString("q")]` | The query string value of that name | `Hardened.Web.Runtime.Attributes` |
| `[FromHeader]`, `[FromHeader("X-Tenant")]` | The request header of that name | `Hardened.Web.Runtime.Attributes` |
| `[FromCookie]`, `[FromCookie("session")]` | The cookie of that name | `Hardened.Web.Runtime.Attributes` |
| `[FromForm]`, `[FromForm("user_name")]` | A field or file of a form body | `Hardened.Web.Runtime.Attributes` |
| `[FromBody]` | The request body | `Hardened.Requests.Abstract.Attributes` |
| `[FromServices]` | The container | `Hardened.Requests.Abstract.Attributes` |
| An attribute that implements `ICustomBindingAttribute` | The value the attribute returns. See [Custom binding attributes](#custom-binding-attributes) | `Hardened.Requests.Abstract.Attributes` |

The examples on this page need no package beyond the two that the template's library project
references, `Hardened.Shared.Runtime` and `Hardened.Web.Runtime`.

An attribute with no name argument reads the name of the parameter. `[FromHeader] string tenant`
reads the header `tenant`. A parameter named after a C# keyword binds by the name without the `@`.
`[FromQueryString] string? @event` reads `?event=`.

[Forms and files](/guide/forms) covers `[FromForm]`. [Registering services](/guide/services) covers
`[FromServices]`.

## Parameters without a binding attribute

A parameter with no binding attribute binds from the first row of this table that matches it:

| The parameter | Binds from |
|---|---|
| `IExecutionContext` | The request's execution context |
| `IExecutionRequest` | The request |
| `IExecutionResponse` | The response |
| `IServiceProvider` | The request's service scope |
| `CancellationToken` | The request's cancellation token |
| Any other interface | The container |
| A name that matches a route token | The path token |
| Anything else | The request body |

`IExecutionContext`, `IExecutionRequest` and `IExecutionResponse` are in the namespace
`Hardened.Requests.Abstract.Execution`. [The execution pipeline](/guide/execution-pipeline) covers
their members.

An unattributed `string`, `int` or other value binds from the body when its name matches no route
token. On a POST handler, `string name` receives `ada` from the JSON body `"ada"`.

The build reports warning `HRDR010` for a value on a GET handler that has no `[FromQueryString]`.
Every request to that handler that sends no body answers 400. With `[FromQueryString]` removed from
`title`, the first example builds with this warning:

```text
CSC : warning HRDR010: Parameter 'title' of 'SearchController.Find' is read from the request body, and a GET carries none, so a request that sends no body is refused before the handler runs and the published document gives the operation a body it should not have. Bind 'title' with [FromQueryString] or [FromHeader], mark it [FromServices] if it is a service, or suppress HRDR010 if this operation deliberately reads a body from a GET.
```

[Registering services](/guide/services) covers a parameter typed as a class that is a service, and
`HRDR007`. [Routing](/guide/routing) covers path tokens and `HRDR005`.

## Names and decoding

The source decides how a name matches:

| Source | Names are compared |
|---|---|
| Query string | Exactly, case included |
| Header | In any case |
| Cookie | Exactly, case included |
| Path token | Exactly, case included. See [Routing](/guide/routing) |

A query value is percent-decoded. A `+` in it is a space. `?name=Buy+milk%21` binds `Buy milk!`.
`%2B` binds a plus.

A cookie value arrives as sent, with no decoding. `Cookie: session=a%20b` binds `a%20b`.

A query name with no `=` counts as an empty value.

## Required, optional and default values

The declaration of a `[FromQueryString]` parameter decides what it receives:

| Declared | Absent, or sent empty | Sent, and not an `int` |
|---|---|---|
| `int count` | 400 `required` | 400 `invalid` |
| `int? count` | `null` | 400 `invalid` |
| `int count = 5` | `5` | 400 `invalid` |

An empty value counts as absent. `?count=` is the same as no `count`. A value that does not convert
never falls back to the default.

A nullable reference type is optional. `string? title` is `null` when `title` is absent.

The binder stops at the first parameter that fails. The 400 names only that parameter's field.

Headers, cookies and form fields follow the same rules. [Validation](/guide/validation) covers the
400 body, constraints on parameters and changing the status.

## Type conversion

The binder converts text to these parameter types:

| Parameter type | Accepts |
|---|---|
| `string` | The value as sent |
| `bool` | `true` or `false`, in any case |
| `int`, `long`, `short`, `byte`, `sbyte`, `uint`, `ulong`, `ushort`, `float`, `double`, `decimal` | A number in the invariant culture |
| `char` | One character |
| `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan` | A date or time in the invariant culture, such as `2026-09-23`, `2026-09-23T10:00:00+02:00` or `01:30:00` |
| `Guid` | A GUID |
| `Uri` | An absolute or relative URI |
| An enum | The value's wire name |
| The nullable form of each | The same |

Numbers and dates parse in the invariant culture. `1.5` is one and a half. `1,5` binds 15, because
the comma is read as a thousands separator.

`bool` refuses `1` with 400 `invalid`. `DateOnly` accepts the month-first form `09/23/2026`. It
refuses `23/09/2026`.

An enum value is read by its wire name, the name the JSON body uses. The wire name is camelCase
unless `[JsonEnumNaming]` sets another naming. The binder matches the wire name exactly. For
`Priority.InProgress`, `inProgress` binds. `InProgress`, `in-progress` and `2` answer 400 `invalid`.
A path token binds an enum the same way. [JSON serialization](/guide/json) covers
`[JsonEnumNaming]`.

### Collections

A parameter declared as an array, `List<T>`, `IList<T>`, `ICollection<T>`, `IEnumerable<T>`,
`IReadOnlyList<T>` or `IReadOnlyCollection<T>` of a type in the table above receives every value
sent under its name. Each repeat of the name adds an item. A comma inside a value separates two
items. `?ids=1&ids=2,3` binds `[1, 2, 3]`.

The binder drops an empty item. `?ids=1,,3` binds `[1, 3]`.

Headers work the same way. `X-Tag: red, green` and two `X-Tag` lines both bind `red` and `green` to
`[FromHeader("X-Tag")] List<string>? tags`.

A nullable collection is `null` when the name is absent. It is empty for `?ids=`. A collection that
is not nullable answers 400 `required` when no item was sent.

A parameter that is not a collection receives every value sent under its name, joined with commas.
`?name=a&name=b` binds `a,b` to a `string`. `?count=1&count=2` answers 400 `invalid` for an `int`.

### A type of your own

`IStringConverter` converts text to one type, which its `ConvertType` names. It is in the namespace
`Hardened.Requests.Abstract.Serializer`. A class that implements it and carries `[SingletonService]`
is registered as an `IStringConverter`. The binder then uses it for that type in a path token, a
query value, a header, a cookie or a form field.

Give that type a public static `Parse` or `TryParse` whose first parameter is a `string`. Without
one, `[FromQueryString]` and `[FromForm]` bind the type as a [model](#models-from-the-query-string),
one value per member. The binder never calls that `Parse` method. A type that has `Parse` and no
registered converter answers 400 `invalid` on every request.

In this example, `DateRangeConverter` converts text to a `DateRange`:

```csharp
using System.Globalization;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Serializer;

namespace Todos;

public readonly record struct DateRange(DateOnly From, DateOnly To)
{
    public static DateRange Parse(string value)
    {
        var parts = value.Split("..");

        return new DateRange(
            DateOnly.Parse(parts[0], CultureInfo.InvariantCulture),
            DateOnly.Parse(parts[1], CultureInfo.InvariantCulture)
        );
    }
}

[SingletonService]
public class DateRangeConverter : IStringConverter
{
    public Type ConvertType => typeof(DateRange);

    public T Convert<T>(string value) => (T)(object)DateRange.Parse(value);
}
```

A handler binds a `DateRange` from the query string:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public record DueWindow(DateOnly From, DateOnly To, int Days);

[BasePath("/due")]
public class DueController
{
    [Get("/")]
    public DueWindow Window([FromQueryString] DateRange range) =>
        new(range.From, range.To, range.To.DayNumber - range.From.DayNumber + 1);
}
```

```http
GET /todos/due?range=2026-09-01..2026-09-30

HTTP/1.1 200 OK
Content-Type: application/json

{"from":"2026-09-01","to":"2026-09-30","days":30}
```

A converter that throws answers 400 `invalid`:

```http
GET /todos/due?range=soon

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"range","code":"invalid","message":"range is not a valid DateRange."}]}
```

## Models from the query string

`[FromQueryString]` on a parameter whose type is a model binds one query value per member:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public record TodoFilter(bool? Done, string? Title, int Page = 1, int Size = 20);

public record TodoPage(int Page, int Size, IReadOnlyList<Todo> Todos);

[BasePath("/filter")]
public class FilterController
{
    [Get("/")]
    public async Task<TodoPage> Find(ITodoStore store, [FromQueryString] TodoFilter filter)
    {
        var todos = await store.All();

        var matching = todos
            .Where(todo => filter.Done is null || todo.Done == filter.Done)
            .Where(todo => filter.Title is null || todo.Title.Contains(filter.Title))
            .Skip((filter.Page - 1) * filter.Size)
            .Take(filter.Size)
            .ToList();

        return new TodoPage(filter.Page, filter.Size, matching);
    }
}
```

```http
GET /todos/filter?done=false&size=10

HTTP/1.1 200 OK
Content-Type: application/json

{"page":1,"size":10,"todos":[{"id":2,"title":"Add an endpoint","done":false}]}
```

`[FromForm]` binds a model from a form body by the same rules. [Forms and files](/guide/forms)
covers form models.

A model is a class or a struct that is not abstract, is outside the `System` and `Microsoft`
namespaces, is not a collection, and has no static `Parse` or `TryParse`. Any other type binds as
one value.

A member binds from the name in its `[JsonPropertyName]`. `PageSize` with
`[JsonPropertyName("page_size")]` binds from `page_size`. A member without the attribute binds from
its name with the first letter lower-cased. The names match exactly, case included. `?pageSize=5`
does not bind `page_size`. `?Size=1` does not bind `size`.

The binder calls the constructor marked `[JsonConstructor]`. Without one, it calls the public
parameterless constructor. Without that, it calls the only public constructor.

The constructor's parameters bind first. A constructor parameter takes the name of the property
whose name matches it, ignoring case. Then every public property with a public `set` or `init`
binds, unless a constructor parameter covered it. Inherited properties bind too. The binder skips a
member marked `[JsonIgnore]`, unless its `Condition` is one of the `WhenWriting` values.

A member with no default is required when its type is not nullable, when it carries `[Required]`
from `ValidationModules.Constraints`, or when it is declared `required`. A missing required member
answers 400 `required`, naming its field. An absent member takes the constructor parameter's
default. A property with an initializer keeps the initializer's value when its field is absent or
sent empty.

A collection member takes every value sent under its name, as a collection parameter does. An enum
member reads the wire name. A struct model binds the same way.

The model's constraints are checked after the binder builds it, as a body model's are.

### Models that fail the build

Error `HRDW007` fails the build for a `[FromQueryString]` or `[FromForm]` model that these rules
cannot build. Its message starts
`'<Class>.<Method>' binds '<parameter>' from the query string one field per member, but`. A form
model's message has `form` in place of `query string`. The rest of the message depends on the
model:

| The model | The end of the `HRDW007` message |
|---|---|
| A member whose type is a model | `its member 'Address' is a 'Todos.Address', and a field carries a value rather than an object.` |
| The parameter is nullable: `TodoFilter? filter` | `it is nullable, and a model bound from fields is constructed whether or not any field was sent.` |
| The attribute names a field: `[FromQueryString("f")]` | `a model takes its field names from its members, so the attribute cannot name a field for it.` |
| An `init` or `required` property with an initializer | `its member 'Size' is init-only and has an initializer, which an absent field would overwrite. Give it a setter, or make it a constructor parameter with a default.` |
| Several public constructors, none marked `[JsonConstructor]` | `'TwoPublic' has more than one public constructor, and none is marked [JsonConstructor].` |
| No public constructor | `'NoPublic' has no public constructor.` |
| Nothing to bind | `'NothingToBind' has no constructor parameters or settable properties to bind.` |
| An `IFormFile` member, on a query string model | `its member 'File' is a file, and only a multipart form carries one.` |

With `TodoFilter? filter` in the example, the build fails with:

```text
CSC : error HRDW007: 'FilterController.Find' binds 'filter' from the query string one field per member, but it is nullable, and a model bound from fields is constructed whether or not any field was sent.
```

## The request body

The deserializer that the request's `Content-Type` selects reads the body.
[Content negotiation](/guide/content-negotiation) covers the deserializers, including the one that
reads a body whose `Content-Type` none of them claims.

The declaration of the body parameter decides the result for an empty body and for the JSON `null`:

| The body | `NewTodo todo` | `NewTodo? todo` |
|---|---|---|
| Empty | 400 `required` | 400 `required` |
| The JSON `null` | 400 `required` | `null` |

The 400 for an empty body names the parameter. A body that does not parse answers 400 `invalid`,
naming the parameter, with the parser's message.

A handler has one body parameter. A second one fails the build with error `HRDR009`. For a handler
`Add(NewTodo request, string note)` on a class `NoteController`, the build reports:

```text
CSC : error HRDR009: 'NoteController.Add' reads 'request' and 'note' from the request body, and a request carries one body. A parameter that names no route token and is not an interface binds from the body: mark a service [FromServices] or type it as the interface it is registered against, and bind a value with [FromQueryString], [FromHeader], [FromForm] or a route token.
```

On the Kestrel and ASP.NET Core hosts, a request body over Kestrel's limit answers 500. The limit is
30,000,000 bytes by default. On the Kestrel host, `Limits.MaxRequestBodySize` in the callback that
`HardenedKestrelApplication.Create` takes raises the limit. This call raises it to 50,000,000 bytes.
It replaces the call in the template's `src/Todos.Host/Program.cs`:

```csharp
await using var app = HardenedKestrelApplication.Create(
    services,
    kestrel =>
    {
        kestrel.ListenAnyIP(port);
        kestrel.Limits.MaxRequestBodySize = 50_000_000;
    }
);
```

## The body as bytes or a stream

A body parameter declared `byte[]` or `Stream` receives the body as sent. No deserializer reads it.
The `Content-Type` does not matter.

| Declared | Receives | A request with no body |
|---|---|---|
| `byte[]` | The whole body, read before the handler runs | 400 `required`, naming the parameter |
| `byte[]?` | The whole body, read before the handler runs | `null` |
| `Stream` | The transport's body, unread | A stream that reads zero bytes |

The handler reads the stream. The stream is readable only while the request is.

In this handler, `content` receives the body:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public record Saved(string Name, int Bytes);

[BasePath("/attachments")]
public class AttachmentController
{
    [Put("/{name}")]
    public Saved Save(string name, byte[] content) => new(name, content.Length);
}
```

```http
PUT /todos/attachments/notes.txt
Content-Type: text/plain

Call the bank

HTTP/1.1 200 OK
Content-Type: application/json

{"name":"notes.txt","bytes":13}
```

The same request with no body answers 400 `required`:

```http
PUT /todos/attachments/notes.txt
Content-Type: text/plain

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"content","code":"required","message":"content is required."}]}
```

## Custom binding attributes

An attribute that implements `ICustomBindingAttribute` binds the parameter it is on. The binder
calls the attribute's `BindValue<T>`. The handler receives the value that `BindValue<T>` returns.

`FromSubdomainAttribute` binds the subdomain of the `Host` header:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;

namespace Todos;

[AttributeUsage(AttributeTargets.Parameter)]
public class FromSubdomainAttribute : Attribute, ICustomBindingAttribute
{
    public ValueTask<T> BindValue<T>(IExecutionContext context, IExecutionRequestParameter parameter)
    {
        if (typeof(T) != typeof(string))
        {
            throw new NotSupportedException($"[FromSubdomain] cannot bind '{parameter.Name}', a {parameter.Type.Name}.");
        }

        var host = context.Request.Headers.TryGetValue("Host", out var value) ? value.ToString() : "";

        var dot = host.IndexOf('.');

        return ValueTask.FromResult((T)(object)(dot > 0 ? host[..dot] : ""));
    }
}
```

A handler puts it on a parameter:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[BasePath("/tenant")]
public class TenantController
{
    [Get("/")]
    public string Current([FromSubdomain] string tenant) => tenant;
}
```

```http
GET /todos/tenant
Host: acme.todos.example

HTTP/1.1 200 OK
Content-Type: application/json

"acme"
```

`BindValue<T>` receives the request's `IExecutionContext` and an `IExecutionRequestParameter`, which
gives the parameter's `Name`, `Index` and `Type`. Both types are in
`Hardened.Requests.Abstract.Execution`. `T` is the parameter's declared type.

The binder constructs the attribute on every request, with the constructor arguments written on the
parameter.

::: warning
Pass a custom binding attribute's settings through its constructor. A property set where the
attribute is applied, such as `[Configured(Value = "set")]`, does not reach the attribute that
binds. `BindValue<T>` sees the property's initial value. The build reports nothing.
:::

The build treats any other attribute on a parameter as a custom binding attribute when no binding
attribute comes before it. The build excludes the constraint attributes of
`ValidationModules.Constraints` and `System.ComponentModel.DataAnnotations`, and
`[EnumeratorCancellation]`. Unless such an attribute implements `ICustomBindingAttribute`, every
request to that handler answers 500. The log names the attribute. For `[Description]`, the log
reads
`Attribute type System.ComponentModel.DescriptionAttribute does not implement ICustomBindingAttribute`.
The build reports nothing. The build ignores the same attribute when it comes after a binding
attribute.

## Diagnostics

The build reports three diagnostics for parameter binding:

| Code | Severity | Reports |
|---|---|---|
| [`HRDR009`](#the-request-body) | Error | More than one parameter binds from the request body |
| [`HRDR010`](#parameters-without-a-binding-attribute) | Warning | A parameter binds from the body of a GET, HEAD, OPTIONS or TRACE handler |
| [`HRDW007`](#models-that-fail-the-build) | Error | A `[FromQueryString]` or `[FromForm]` model the binder cannot build |

The three diagnostics carry no file location. Each message names the class, the method and the parameter. The
build does not report `HRDR010` for a parameter that `HRDR005` or `HRDR007` already names.

[Routing](/guide/routing) covers `HRDR005`. [Registering services](/guide/services) covers
`HRDR007`. [Forms and files](/guide/forms) covers `HRDW002` and `HRDW008`.
[Diagnostics](/reference/diagnostics) lists every code.

## Limits

A model member whose name starts with more than one capital letter binds from a name that the JSON
body does not use. `ID` binds from `?iD=`. `URLPath` binds from `?uRLPath=`. The JSON body writes
`id` and `urlPath`. `[JsonPropertyName]` on the member sets one name for both.

A model declared in a referenced project loses its property initializers. Each property with an
initializer and a non-nullable type is required instead. A constructor parameter's default still
applies.

A body parameter typed as a class derived from `Stream`, such as `MemoryStream`, fails the build
with `CS0266` in the generated binder:
`Cannot implicitly convert type 'System.IO.Stream' to 'System.IO.MemoryStream'`. Declare it
`Stream`.

## Next

| Page | Covers |
|---|---|
| [Forms and files](/guide/forms) | Form fields, files and form models |
| [Validation](/guide/validation) | Constraints on bound values, and the 400 body |
| [Registering services](/guide/services) | Services as handler parameters, and `[FromServices]` |
| [Routing](/guide/routing) | Path tokens and route constraints |
| [Registered routes](/guide/registered-routes) | How the parameters of a lambda bind |
