---
layout: home

hero:
  name: Hardened
  text: A .NET framework for HTTP APIs and serverless functions
  tagline: Source generators write the routing table, the parameter binding and the service registration during the build.
  image:
    src: /hero.svg
    alt: Attributed handlers on the left becoming a generated route table on the right
  actions:
    - theme: brand
      text: Get started
      link: /guide/getting-started
    - theme: alt
      text: Project templates
      link: /guide/project-templates
    - theme: alt
      text: Triggers
      link: /guide/triggers
    - theme: alt
      text: View on GitHub
      link: https://github.com/ipjohnson/Hardened.Framework

features:
  - title: Kestrel
    details: <code>[KestrelRuntime]</code> runs the application on Kestrel, without the ASP.NET Core request pipeline.
    link: /guide/hosts
    linkText: Hosts
  - title: ASP.NET Core
    details: <code>[AspNetCoreRuntime]</code> and <code>app.UseHardened()</code> serve the routes as middleware in an ASP.NET Core application.
    link: /guide/hosts
    linkText: Hosts
  - title: AWS Lambda
    details: <code>[LambdaHttpModule]</code> runs the application as a Lambda function behind an API Gateway HTTP API or a function URL.
    link: /aws/lambda-web
    linkText: Web applications
  - title: Google Cloud Run
    details: <code>[CloudRunRuntime]</code> runs the application as a Cloud Run service, on Kestrel.
    link: /gcp/web
    linkText: Web services
  - title: Google Cloud Functions
    details: <code>Hardened.Gcp.Functions.Runtime</code> runs a <code>[CloudRunRuntime]</code> application as a Cloud Functions 2nd gen function.
    link: /gcp/web
    linkText: Web services
  - title: Azure Functions
    details: <code>[HttpModule]</code> serves the routes through one HTTP function in the .NET isolated worker.
    link: /azure/web
    linkText: Web applications
---

## A handler

`dotnet new hardened-web -n Todos` writes an HTTP API, from the template that
[Getting started](/guide/getting-started) installs. Its library project, `src/Todos`, holds the
handlers in `src/Todos/TodoController.cs`. The block below shows one of the file's four handlers.

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

`dotnet run --project src/Todos.Host` starts the application on port 5080.

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

```http
GET /todos/0

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"id","code":"range","message":"id must be at least 1."}]}
```

A route attribute on a method makes the method a handler. `TodoController` has no base type and no
interface. The generated code registers it.

| Declaration | Effect |
|---|---|
| `[Get("/{id}")]` | Routes `GET /todos/{id}` to `ById`. The `/todos` prefix is the base path of the library module, `TodosLibrary` |
| `Response<Todo, NotFound>` | Declares a 200 with a `Todo` and a 404 with a `NotFound` |
| `[Range(Min = 1)]` | Is checked before the handler runs. A request that fails the check gets a 400 that names the field and the rule |
| `[Operation("getTodo")]` | Names the operation in the OpenAPI document |

The document at `/openapi.json` lists 200, 400 and 404 for `GET /todos/{id}`.

During the build, a source generator writes a handler class for `ById`. It binds `id` from the path
and `store` from the container. `TodoStore` in `src/Todos/TodoStore.cs` carries
`[SingletonService]`, which registers it as `ITodoStore`.

## The application class

`src/Todos.Host/Application.cs` is the application class. It names the host and imports the library
module.

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

| Attribute | Effect |
|---|---|
| `[HardenedModule]` | Makes the `partial` class a module. The generator writes the other half of the class, including `PopulateServiceCollection` |
| `[KestrelRuntime]` | Runs the application on Kestrel |
| `[HardenedOpenApiUi(Title = "Todos", Environments = "development")]` | Serves a reference page for the API at `/docs` in the `development` environment |
| `[TodosLibrary]` | Imports the library module. The generator writes the `TodosLibraryAttribute` class from the module |

The environment is `development` when the environment variable `HARDENED_ENVIRONMENT` is not set.
`src/Todos.Host/Program.cs` starts the host. [Hosts](/guide/hosts) shows it for each host.

## A test

`dotnet new hardened-web -n Todos` also writes this test method in
`tests/Todos.Tests/TodoStoreMockTests.cs`.

```csharp
using DependencyModules.Testing.Attributes;
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Runtime.Responses;
using Hardened.Web.Testing;
using NSubstitute;
using Todos.Client;
using Xunit;

namespace Todos.Tests;

public class TodoStoreMockTests
{
    [ModuleTest]
    public async Task GetTodo_ReadsTheMockedStore(TodosClient client, [Mock] ITodoStore store)
    {
        store.Find(1).Returns(new Todo(1, "from the mock", false));

        var todo = await client.Todos[1].GetAsync().Returns<Ok<ClientModels.Todo>>();

        Assert.Equal("from the mock", todo.Value.Title);
    }
}
```

In the template, `TodoStoreMockTests.cs` has one `using` line, `using NSubstitute;`.
`tests/Todos.Tests/Usings.cs` declares the block's other namespaces as global usings. It also
declares `ClientModels` as an alias for `Todos.Client.Models`.

`TodosClient` is the Kiota client that `src/Todos.Client` generates from the OpenAPI document. The
client sends its requests into the application in process, with no socket.

| Declaration | Effect |
|---|---|
| `[ModuleTest]` | Builds the application for the test and passes the method's parameters from it |
| `[assembly: HardenedTestEntryPoint(typeof(TodosLibrary))]` | Names the module under test, the library module |
| `[assembly: KiotaTesting]` | Makes the client a test parameter |
| `[Mock]` | Replaces the application's `ITodoStore` with an NSubstitute double. The handler reads the todo the test set up |
| `[assembly: NSubstituteSupport]` | Selects NSubstitute for `[Mock]` |
| `Returns<Ok<ClientModels.Todo>>()` | Asserts that the call answered 200 with a `Todo`. It returns the `Ok<T>`, whose `Value` is the body |

The table's three assembly attributes are in `tests/Todos.Tests/Bootstrap.cs`. The template's tests
pass when `dotnet test` runs in the solution directory. [Writing a test](/guide/testing) covers the
testing attributes and parameters.

## Hosts

The application class names its host with one attribute from the host's package. `--host` on
`dotnet new hardened-web` selects the host.

| Host | `--host` | Application attribute |
|---|---|---|
| Kestrel | `kestrel` | `[KestrelRuntime]` |
| ASP.NET Core | `aspnet` | `[AspNetCoreRuntime]` |
| AWS Lambda | `aws-lambda` | `[LambdaHttpModule]` |
| Google Cloud Run | `cloud-run` | `[CloudRunRuntime]` |
| Google Cloud Functions | None | `[CloudRunRuntime]` |
| Azure Functions | `azure-functions` | `[HttpModule]` |

The template has no `--host` value for Google Cloud Functions. [Hosts](/guide/hosts) describes how
to change a Cloud Run host project into a Cloud Functions one.

For every `--host` value, the template writes the same `src/Todos`, with the same handler, and the
same test. The test above passes with each of the five values. On each host in the table,
`GET /todos/1` answers with the 200 and the body shown above.

The host project, `src/Todos.Host`, holds each host's package references and its own `Program.cs`.
[Hosts](/guide/hosts) covers them.

A trigger attribute, such as `[Queue("orders")]`, makes a method a handler for a queue, a topic, a
schedule or another event source, on AWS Lambda, Google Cloud Run or Azure Functions.
[Triggers](/guide/triggers) covers them.

## Next

| Page | Covers |
|---|---|
| [Getting started](/guide/getting-started) | Running the template, calling the API and running its tests |
| [Project templates](/guide/project-templates) | The template options, including `--host` |
| [Hosts](/guide/hosts) | Each host's packages and `Program.cs` |
| [Writing a test](/guide/testing) | The testing attributes and parameters |
| [Triggers](/guide/triggers) | Handlers for queues, topics, schedules and other event sources |
