# Getting started

A Hardened application starts from the `hardened-web` template. The template writes an HTTP API: a
library for the handlers, a host project, a client project and a test project.

```bash
dotnet new install Hardened.Templates
dotnet new hardened-web -n Todos
cd Todos
dotnet run --project src/Todos.Host
```

`dotnet new install Hardened.Templates` installs the templates from nuget.org.
`dotnet run --project src/Todos.Host` starts the API on port 5080.

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

## Requirements

The solution's `global.json` pins the .NET 8 SDK, version 8.0.401 or a later 8.0 release.

## Run the API

The host logs its address and the address of a reference page for the API:

```console
$ dotnet run --project src/Todos.Host
info: Todos.Host[0] Listening on http://localhost:5080
info: Todos.Host[0] Browse http://localhost:5080/docs to access your API.
```

The reference page is at `/docs`. The host serves it only in the `development` environment. The
environment is `development` when the environment variable `HARDENED_ENVIRONMENT` is not set.

The OpenAPI document is at `/openapi.json`. [The OpenAPI document](/guide/openapi-document) covers
the document and the reference page.

## The handler

`src/Todos/TodoController.cs` holds the handlers. This excerpt shows one of them:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Operation("getTodo")]
    [Get("/{id}")]
    public async Task<Response<Todo, NotFound>> ById(ITodoStore store, [Range(Min = 1)] int id)
    {
        var todo = await store.Find(id);

        if (todo is null)
        {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return todo;
    }
}
```

A route attribute on a method makes the method a handler. `[Get("/{id}")]` routes `GET /todos/{id}`
to `ById`. The `/todos` prefix is the library module's base path.

The generator binds `id` from the path and `store` from the container. `TodoStore` in
`src/Todos/TodoStore.cs` carries `[SingletonService]`. The attribute registers the class as
`ITodoStore`. [Registering services](/guide/services) covers the lifetime attributes.

`[Range(Min = 1)]` is checked before the handler runs. `GET /todos/0` gets a 400.

The return type declares a 200 with a `Todo` and a 404 with a `NotFound`. `[Operation("getTodo")]`
names the operation in the OpenAPI document. The document lists 200, 400 and 404 for
`GET /todos/{id}`.

[Routing](/guide/routing), [Parameter binding](/guide/parameter-binding),
[Declared responses](/guide/responses) and [Validation](/guide/validation) cover these parts of the
handler.

## The modules

`src/Todos/TodosLibrary.cs` is the library module. It holds the routes:

```csharp
using DependencyModules.Runtime.Interfaces;
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
    }
}
```

`src/Todos.Host/Application.cs` is the application module. It names the host and imports the
library:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.OpenApi;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[HardenedOpenApiUi(Title = "Todos", Environments = "development")]
[TodosLibrary]
public partial class Application;
```

The two modules have these attributes:

| Attribute | On | Does |
|---|---|---|
| `[HardenedModule]` | Both | Makes the `partial` class a module |
| `[HardenedWebModule]` | `TodosLibrary` | Serves the routes in the library's assembly |
| `[BasePath("/todos")]` | `TodosLibrary` | Prefixes every route in the library's assembly |
| `[Server("http://localhost:5080", "Local")]` | `TodosLibrary` | Names the server in the OpenAPI document |
| `[Enable<OpenApiDocumentPublishing>]` | `TodosLibrary` | Serves the OpenAPI document at `/openapi.json` |
| `[KestrelRuntime]` | `Application` | Runs the application on Kestrel |
| `[HardenedOpenApiUi(Title = "Todos", Environments = "development")]` | `Application` | Serves the reference page at `/docs` in `development` |
| `[TodosLibrary]` | `Application` | Imports the library module |

[Modules](/guide/modules) covers modules.

`TodosLibrary.ConfigureServices` registers `TodosJsonContext` as the JSON type resolver.
[JSON serialization](/guide/json) covers this registration.

`src/Todos.Host/Program.cs` starts the host. [Hosts](/guide/hosts) shows `Program.cs` for each host.

## The tests

`dotnet test` in the solution directory runs the tests in `tests/Todos.Tests`:

```bash
dotnet test
```

The tests pass. This test is from `tests/Todos.Tests/TodoTests.cs`:

```csharp
[HardenedTest]
public async Task GetTodo_ReturnsTheTodo(TodosClient client)
{
    var todo = await client.Todos[1].GetAsync().Returns<Ok<ClientModels.Todo>>();

    Assert.Equal("Read the generated code", todo.Value.Title);
}
```

`[HardenedTest]` builds the application for the test and supplies its parameters. `TodosClient` is
the client that `src/Todos.Client` generates from the OpenAPI document.
`tests/Todos.Tests/Bootstrap.cs` holds the assembly attributes that set up the tests.
`[assembly: KiotaTesting]` among them makes the client a test parameter. In a test, the client sends
its requests into the application in process, with no socket.
`Returns<Ok<ClientModels.Todo>>()` asserts that the call answered with that declared response.

`tests/Todos.Tests/Usings.cs` declares the namespaces the tests use as global usings.

[Writing a test](/guide/testing) covers the testing attributes and parameters.

## Next

| Page | Covers |
|---|---|
| [Project templates](/guide/project-templates) | The other templates, and the options for the host, the contract, the response model, the client and the serializer |
| [From scratch](/guide/from-scratch) | The same kind of application assembled from packages, and the code the build generates |
| [Hosts](/guide/hosts) | The other hosts and their `Program.cs` |
| [Routing](/guide/routing) | Route attributes and path tokens |
| [Writing a test](/guide/testing) | The testing attributes and parameters |
