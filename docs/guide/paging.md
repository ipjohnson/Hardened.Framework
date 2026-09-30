# Paging

`Page<T>` is one page of a list and the token that fetches the next page. `IPageTokens` turns the
position a page ended at into that token, and reads it back from the next request.

The examples add this handler to the `hardened-web` template, in
`src/Todos/PagedTodoController.cs`. Its route answers under `/todos`, the `[BasePath("/todos")]` on
the template's library module.

```csharp
using Hardened.Requests.Abstract.Paging;
using Hardened.Web.Runtime.Attributes;
using ValidationModules.Constraints;

namespace Todos;

public record TodoCursor(int Id);

public class PagedTodoController
{
    [Get("/paged")]
    public async Task<Page<Todo>> Paged(
        ITodoStore store,
        IPageTokens pageTokens,
        [FromQueryString] string? pageToken,
        [FromQueryString] [Range(Min = 1, Max = 100)] int pageSize = 20)
    {
        var after = pageTokens.Decode<TodoCursor>(pageToken);
        var todos = await store.All();

        var rows = todos
            .Where(todo => after is null || todo.Id > after.Id)
            .OrderBy(todo => todo.Id)
            .Take(pageSize + 1)
            .ToList();

        return Page.From(rows, pageSize, last => pageTokens.Encode(new TodoCursor(last.Id)));
    }
}
```

```http
GET /todos/paged?pageSize=1

HTTP/1.1 200 OK
Content-Type: application/json

{"items":[{"id":1,"title":"Read the generated code","done":true}],"nextPageToken":"eyJpZCI6MX0"}
```

The client sends the token back to get the next page. The last page's token is `null`:

```http
GET /todos/paged?pageSize=1&pageToken=eyJpZCI6MX0

HTTP/1.1 200 OK
Content-Type: application/json

{"items":[{"id":2,"title":"Add an endpoint","done":false}],"nextPageToken":null}
```

`Page<T>`, `Page` and `IPageTokens` are in `Hardened.Requests.Abstract.Paging`, in the package
`Hardened.Requests.Abstract`. Nothing has to be registered. Every application has `IPageTokens`
as a singleton.

## Building a page

`Page.From(rows, pageSize, nextPageToken)` takes up to `pageSize` + 1 rows in page order. When
`rows` holds more than `pageSize`, the page holds the first `pageSize` rows and calls
`nextPageToken` with the last of them. The extra row says that a next page exists, and it is not
returned. When `rows` fits the page, the page has no token. A client that reaches a full last page
therefore does not ask for an empty one.

A `pageSize` below 1 throws `ArgumentOutOfRangeException`.

The cursor is where the page ended, such as the last row's id, rather than a count of rows to skip.
A row inserted or removed before that position does not move the next page.

## Page tokens

A code-first handler takes `IPageTokens` as a parameter. A parameter typed as an interface binds
from the container. [Parameter binding](/guide/parameter-binding) covers it. A handler that
implements an interface generated from a contract takes `IPageTokens` through its constructor.

`Encode(cursor)` writes the cursor as JSON with the application's `IJsonSerializer`, then encodes
the JSON as base64url. `eyJpZCI6MX0` above is `{"id":1}`.

`Decode<TCursor>(pageToken)` reads the cursor back. A null or empty token reads as `default`, which
is `null` for a class such as `TodoCursor`. The first request sends no token, so it reads `null`.
For a cursor that is a value type, `Decode<long?>` tells no token from a zero.

A token that does not decode answers 400 with the code `invalid`:

```http
GET /todos/paged?pageToken=abc!

HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{"errors":[{"field":"pageToken","code":"invalid","message":"pageToken is not a valid page token."}],"detail":"One or more validation errors occurred.","type":"urn:hardened:problem:validation-failed","title":"Request Validation Failed","status":400}
```

`Decode` throws `ValidationException`, so the handler stops there. The field is the argument as the
handler wrote it. The compiler passes the expression to `Decode`, so
`Decode<TodoCursor>(nextToken)` names `nextToken`. A token is refused when it is not base64url, when
its JSON does not read as `TCursor`, or when its signature does not match.

## Signing tokens

Without a key, a client can decode a token, change the cursor and send it back. The environment
variable `HARDENED_PAGE_TOKEN_KEY` sets a key, and every token is signed with it:

```bash
HARDENED_PAGE_TOKEN_KEY=... dotnet run --project src/Todos.Host
```

A signed token is the JSON followed by its HMAC-SHA256, encoded together. The key's UTF-8 bytes are
the HMAC key. A token whose HMAC does not match answers the 400 above.

| Token written with | Read with | Result |
|---|---|---|
| No key | No key | Read |
| The key | The same key | Read |
| The key | Another key | 400 |
| No key | A key | 400 |
| A key | No key | 400 |

Every instance that serves the same list needs the same key. Changing the key refuses every token
signed with the old one, and a client holding one starts again from the first page.

`PageTokenConfiguration`, in `Hardened.Requests.Runtime.Paging`, holds the key. An `AppConfig`
amendment sets it from somewhere other than the environment. This
`src/Todos.Host/ApplicationConfiguration.cs` reads it from a mounted secret:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Paging;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Host;

public partial class Application : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        var config = new AppConfig();

        config.Amend(
            (PageTokenConfiguration tokens) =>
                tokens.Key = File.ReadAllText("/run/secrets/page-token-key").Trim()
        );

        services.AddSingleton<IConfigurationPackage>(config);
    }
}
```

[Configuration](/guide/configuration) covers `AppConfig`.

## The published document

`Page<Todo>` publishes as the schema `PageOfTodo`, named by the rule that names every constructed
generic type:

```json
"PageOfTodo": {
  "type": "object",
  "required": ["items"],
  "properties": {
    "items": { "type": "array", "items": { "$ref": "#/components/schemas/Todo" } },
    "nextPageToken": { "type": ["string", "null"] }
  }
}
```

`pageToken` and `pageSize` are ordinary query parameters. `[Range]` publishes as `minimum` and
`maximum`.

## Native AOT

The context declares each closed page type the application returns, and each cursor type.
`Encode` and `Decode` read the cursor's metadata from the same resolvers as a request body:

```csharp
[JsonSerializable(typeof(Page<Todo>))]
[JsonSerializable(typeof(TodoCursor))]
```

A cursor type that no resolver describes throws `NotSupportedException` from `Encode` or `Decode`,
and the request answers 500. [JSON serialization](/guide/json#native-aot) covers the context.

## MessagePack

`Page<T>` has no MessagePack formatter. An operation that answers MessagePack returns a page type of
its own that carries `[MessagePackObject]`. [MessagePack](/guide/message-pack) covers the
formatters.

## Smithy

A Smithy operation names its paging members with `@paginated`. They generate as ordinary members,
and the handler reads and writes the token members with `IPageTokens`.
[Generating from Smithy](/guide/smithy#paging) covers the trait and what the build checks.

## Next

| Page | Covers |
|---|---|
| [Parameter binding](/guide/parameter-binding) | Query parameters, and services as parameters |
| [Validation](/guide/validation) | The 400 body |
| [Configuration](/guide/configuration) | `AppConfig` amendments |
| [JSON serialization](/guide/json) | The context a Native AOT application registers |
| [Generating from Smithy](/guide/smithy#paging) | `@paginated` |
