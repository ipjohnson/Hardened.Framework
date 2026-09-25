# Forms and files

`[FromForm]` on a handler parameter binds it from a field of an `application/x-www-form-urlencoded`
or `multipart/form-data` request body.

```csharp
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Todos;

[BasePath("/form")]
public class TodoFormController
{
    [Post("/")]
    public async Task<Created<Todo>> Create(ITodoStore store, [FromForm] string title)
    {
        var todo = await store.Add(title);

        return new Created<Todo>(todo, $"/todos/{todo.Id}");
    }
}
```

```http
POST /todos/form
Content-Type: application/x-www-form-urlencoded

title=Buy+milk

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3

{"id":3,"title":"Buy milk","done":false}
```

A parameter binds from a form only when it carries `[FromForm]`. The attribute is in the namespace
`Hardened.Web.Runtime.Attributes`. Nothing has to be installed or registered.

The examples on this page run in an application made with `dotnet new hardened-web -n Todos`.
`ITodoStore` and `Todo` are the template's. The library module's `[BasePath("/todos")]` prefixes
every route.

## Fields

A field's name is the attribute's argument, or the parameter's name when there is no argument.
`[FromForm("pass_word")] string password` reads `pass_word`. A field name must match exactly,
including its case. `Title=Wrong+case` does not bind `title`.

A url-encoded value is percent-decoded, with `+` read as a space. `username=Ada+Lovelace` binds
`Ada Lovelace`, `x%40y` binds `x@y`, and `a%2Bb` binds `a+b`. Field values are read as UTF-8.

A field converts to the parameter's type by the rules a query string value follows. A field is
required, optional or defaulted by the same rules. [Parameter binding](/guide/parameter-binding)
covers them.

A field sent more than once fills a collection parameter. `tags=a&tags=b,c` binds `a`, `b` and `c`
to `[FromForm] List<string>? tags`. Two multipart parts named `tags` bind both values.

The request's `Content-Type` decides how the body is read:

| `Content-Type` | The body reads as |
|---|---|
| `application/x-www-form-urlencoded`, with or without parameters such as `; charset=UTF-8` | Url-encoded fields |
| None | Url-encoded fields |
| `multipart/form-data` | Fields and files |
| Any other | Not read. The request answers 415 |

A request with any other `Content-Type` answers 415. The `Accept` header names the two types a form
handler reads:

```http
POST /todos/form
Content-Type: application/json

{"title":"Buy milk"}

HTTP/1.1 415 Unsupported Media Type
Accept: application/x-www-form-urlencoded, multipart/form-data
Content-Type: application/json

{"type":"UnsupportedContentTypeException","message":"This route does not read application/json. It reads application/x-www-form-urlencoded, multipart/form-data.","details":""}
```

## Files

A part of a `multipart/form-data` body that carries a `filename` is a file. A part without one is a
field. `[FromForm] IFormFile file` binds the first file sent under the name `file`. `IFormFile` is in
the namespace `Hardened.Requests.Abstract.Forms`.

This handler, added to `TodoFormController`, adds a todo for each non-empty line of an uploaded
file:

```csharp
using Hardened.Requests.Abstract.Forms;

public record ImportResult(string FileName, long Bytes, int Added);

[Post("/import")]
public async Task<ImportResult> Import(ITodoStore store, [FromForm] IFormFile file)
{
    using var reader = new StreamReader(file.OpenReadStream());

    var added = 0;

    while (await reader.ReadLineAsync() is { } line)
    {
        if (line.Length > 0)
        {
            await store.Add(line);

            added++;
        }
    }

    return new ImportResult(file.FileName, file.Length, added);
}
```

```http
POST /todos/form/import
Content-Type: multipart/form-data; boundary=----todos

------todos
Content-Disposition: form-data; name="file"; filename="todos.txt"
Content-Type: text/plain

Water the plants
Call the bank
------todos--

HTTP/1.1 200 OK
Content-Type: application/json

{"fileName":"todos.txt","bytes":31,"added":2}
```

A file has these members:

| Member | Returns |
|---|---|
| `Name` | The field name the part was sent under |
| `FileName` | The part's `filename` |
| `ContentType` | The part's `Content-Type`, or `text/plain` when it sent none |
| `Length` | The number of bytes |
| `OpenReadStream()` | A read-only stream over the bytes, from the start |

`OpenReadStream` returns a new stream over the same bytes on each call. When a part sends
`filename*` beside `filename`, `FileName` is the `filename` value.

A collection of `IFormFile` takes every file sent under the name, in the order they arrived. The
collection can be an array, `List<T>`, `IList<T>`, `ICollection<T>`, `IEnumerable<T>`,
`IReadOnlyList<T>` or `IReadOnlyCollection<T>`.

When no file was sent under a parameter's name, the declared type decides the result:

| Declared | When no file was sent under the name |
|---|---|
| `[FromForm] IFormFile file` | 400 `required` |
| `[FromForm] IFormFile? file` | `null` |
| `[FromForm] IReadOnlyList<IFormFile> photos` | 400 `required` |
| `[FromForm] IFormFile[]? scans` | `null` |

The import handler answers 400 to a body with no file under `file`:

```http
POST /todos/form/import
Content-Type: multipart/form-data; boundary=----todos

------todos
Content-Disposition: form-data; name="title"

Nothing here
------todos--

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"file","code":"required","message":"file is required."}]}
```

::: warning
A browser that submits a file input with no file chosen sends a part with `filename=""` and no
bytes. That part binds as a file with an empty `FileName` and a `Length` of 0, so a required
`IFormFile` does not answer 400. Check `Length`.
:::

The import handler accepts such a part:

```http
POST /todos/form/import
Content-Type: multipart/form-data; boundary=----todos

------todos
Content-Disposition: form-data; name="file"; filename=""
Content-Type: application/octet-stream


------todos--

HTTP/1.1 200 OK
Content-Type: application/json

{"fileName":"","bytes":0,"added":0}
```

## Form models

`[FromForm]` on a parameter whose type is a model binds one field per member.
[Parameter binding](/guide/parameter-binding) gives the rules: how members are named, which
constructor is called, which members are required, how defaults apply, and `HRDW007`. A member
typed `IFormFile`, `IFormFile?` or a collection of `IFormFile` binds from the file parts sent under
its name.

This handler, added to `TodoFormController`, takes a model with an optional file:

```csharp
using Hardened.Requests.Abstract.Forms;

public record TodoUpload(string Title, IFormFile? Attachment);

public record UploadResult(string Title, string? Attachment, long Bytes);

[Post("/upload")]
public async Task<UploadResult> Upload(ITodoStore store, [FromForm] TodoUpload upload)
{
    await store.Add(upload.Title);

    return new UploadResult(
        upload.Title,
        upload.Attachment?.FileName,
        upload.Attachment?.Length ?? 0
    );
}
```

```http
POST /todos/form/upload
Content-Type: multipart/form-data; boundary=----todos

------todos
Content-Disposition: form-data; name="title"

Fix the gate
------todos
Content-Disposition: form-data; name="attachment"; filename="gate.txt"
Content-Type: text/plain

The hinge is loose.
------todos--

HTTP/1.1 200 OK
Content-Type: application/json

{"title":"Fix the gate","attachment":"gate.txt","bytes":19}
```

A model with an `IFormFile?` member also binds from a url-encoded body. The member is then `null`.
The fields and files of a multipart body can arrive in any order.

## How the body is read

The binder reads the whole form before the handler runs, once for all of the handler's `[FromForm]`
parameters. A multipart body is held in memory. Each file is a slice of that memory.

A file is valid only while the request is. The memory goes back to a pool when the request ends, so
a stream from `OpenReadStream` must not be kept past the response.

## Body size

`FormConfiguration.MaxBodyBytes` caps a form body, url-encoded or multipart. It is 30,000,000
bytes unless the application sets it.

`services.ConfigureForms` in a module's `ConfigureServices` sets the cap. `ConfigureForms` and
`FormConfiguration` are in the namespace `Hardened.Requests.Runtime.Forms`. In the template's
library module, `src/Todos/TodosLibrary.cs`, this `ConfigureServices` sets the cap to 5,000,000
bytes:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Forms;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[Server("http://localhost:5080", "Local")]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

        services.ConfigureForms(forms => forms.MaxBodyBytes = 5_000_000);
    }
}
```

A form body longer than the cap answers 413. A body exactly as long as the cap is read. With the
cap above, a 5,000,001-byte multipart body sent to `POST /todos/form/import` gets this response:

```http
HTTP/1.1 413 Payload Too Large
Content-Type: application/json

{"type":"FormBodyTooLargeException","message":"The form body is longer than 5000000 bytes.","details":""}
```

The host's own request body limit applies first. On the Kestrel and ASP.NET Core hosts it is
30,000,000 bytes by default. A body over it answers 500, whatever the cap is.
[Parameter binding](/guide/parameter-binding) covers raising it.

## Unreadable bodies

A form body that cannot be read answers 400. The error has the code `invalid` and the field
`body`. Its message gives the reason. The import example's body, sent with
`Content-Type: multipart/form-data` and no boundary, gets this response:

```http
HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"body","code":"invalid","message":"The multipart body\u0027s content type has no usable boundary."}]}
```

These bodies cannot be read:

| The body | The error's message |
|---|---|
| `Content-Type` has no `boundary`, or one longer than 70 characters | `The multipart body's content type has no usable boundary.` |
| The boundary does not appear in the body | `The body does not contain its boundary.` |
| A part is cut off before its boundary | `A part does not end with the boundary.` |
| A part's headers never end, or run past 16,384 bytes | `A part's headers do not end, or run past 16384 bytes.` |
| A part's `Content-Disposition` has no `name` | `A part has no Content-Disposition name.` |
| More than 1,024 parts | `The body has more than 1024 parts.` |
| A url-encoded body with more than 1,024 fields. Each repeat of a name counts | `The body has more than 1024 fields.` |

A boundary can be quoted. `boundary="----todos"` reads the same as `boundary=----todos`.

## Reading the whole form

`context.KnownServices.FormReader.ReadForm(context)` returns the request's form as an
`IFormCollection`. A handler reaches the reader through an `IExecutionContext` parameter.
`IFormCollection` is in the namespace `Hardened.Requests.Abstract.Forms`. `IExecutionContext` is in
`Hardened.Requests.Abstract.Execution`.

This handler, added to `TodoFormController`, returns the fields it was sent:

```csharp
using Hardened.Requests.Abstract.Execution;

[Post("/feedback")]
public async Task<Dictionary<string, string>> Feedback(IExecutionContext context)
{
    var form = await context.KnownServices.FormReader.ReadForm(context);

    return form.Keys.ToDictionary(key => key, key => form.Get(key).ToString());
}
```

```http
POST /todos/form/feedback
Content-Type: application/x-www-form-urlencoded

rating=5&comment=Great+app

HTTP/1.1 200 OK
Content-Type: application/json

{"rating":"5","comment":"Great app"}
```

The collection has these members:

| Member | Returns |
|---|---|
| `Count` | The number of distinct field names, files excluded |
| `Get(key)` | The field's values as `StringValues`, or `StringValues.Empty` when it was not sent |
| `Keys` | Every field name, files excluded |
| `GetFile(name)` | The first file sent under the name, or `null` |
| `GetFiles(name)` | Every file sent under the name, in the order they arrived |

`ReadForm` returns an empty collection for a request with no form body, whatever its
`Content-Type`. It does not answer 415. Only a handler with `[FromForm]` parameters does. The form
is read once per request. A second read in the same request returns the same collection.

A parameter typed `IFormCollection` binds from the container. The container has none registered, so
every request to the handler answers 500. The log says
`No service for type 'Hardened.Requests.Abstract.Forms.IFormCollection' has been registered.` The
build reports nothing.

## Diagnostics

| Code | Severity | Reports |
|---|---|---|
| `HRDW002` | Error | A handler binds a `[FromForm]` parameter and a request body parameter |
| `HRDW007` | Error | A form model the binder cannot build. [Parameter binding](/guide/parameter-binding) lists the cases |
| `HRDW008` | Error | An `IFormFile` bound from anywhere but `[FromForm]` |

`HRDW008` names where the file was bound from. It says `the container` for an `IFormFile` with no
attribute. For the other sources it says `the request body`, `the query string`, `the headers`,
`the cookies` or `the path`. A custom binding attribute on an `IFormFile` is not reported. None of
the three diagnostics carries a file location. [Diagnostics](/reference/diagnostics) lists every
code.

The import handler without `[FromForm]` fails the build with this error:

```text
CSC : error HRDW008: 'TodoFormController.Import' binds 'file', a file, from the container. A file only arrives as a part of a multipart form, so bind it with [FromForm].
```

`Create(ITodoStore store, [FromForm] string title, NewTodo todo)` fails the build with this error:

```text
CSC : error HRDW002: 'TodoFormController.Create' binds 'title' with [FromForm] and 'todo' from the request body. There is one body and the two read it differently, so whichever runs second sees a consumed stream. Bind the fields individually with [FromForm], or take the body as a model - not both.
```

## `IFormFile` in a Web SDK project

In a project on `Microsoft.NET.Sdk.Web`, the implicit usings import `Microsoft.AspNetCore.Http`.
That namespace declares an `IFormFile` of its own. `IFormFile` is then ambiguous. The build fails
with `CS0104`. The template's projects use `Microsoft.NET.Sdk`, so `IFormFile` is not ambiguous in
them. A `using` alias in place of `using Hardened.Requests.Abstract.Forms;` fixes the build:

```csharp
using IFormFile = Hardened.Requests.Abstract.Forms.IFormFile;
```

## Limits

A form handler has no anti-forgery check. A page on another site can post a form to it.
[Authentication](/guide/authentication#cookies-and-cross-site-requests) covers what an application
that authenticates by cookie needs.

In a contract-first project, a request body that the contract declares as
`application/x-www-form-urlencoded` is read as JSON. The build reports nothing. A form sent to the
operation answers 400 `invalid`. A contract body declared as `multipart/form-data` is read the same
way.

## Next

| Page | Covers |
|---|---|
| [Parameter binding](/guide/parameter-binding) | Type conversion, required values, and the rules for models |
| [Validation](/guide/validation) | Constraints on fields and models, and the 400 body |
| [Content negotiation](/guide/content-negotiation) | Request bodies read by a deserializer |
| [The OpenAPI document](/guide/openapi-document) | What an operation publishes in the document |
