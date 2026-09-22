# ![Hardened](https://raw.githubusercontent.com/ipjohnson/Hardened.Framework/main/assets/hardened-mark-32.png) Hardened.Framework

Hardened is a .NET framework for HTTP APIs and serverless functions. Source generators write the
service registration, the routing table, the parameter binding and the configuration classes during
the build. Routes and services are not discovered by reflection at startup.

The same handlers run on Kestrel, ASP.NET Core, AWS Lambda, Google Cloud Run and Azure Functions.
The project's package references and the application class select the host.

The documentation is at
[ipjohnson.github.io/Hardened.Framework](https://ipjohnson.github.io/Hardened.Framework/). The
packages are on nuget.org under `Hardened.*`. Every version is a prerelease version, so
`dotnet add package` needs `--prerelease`.

## Requirements

- The .NET 8 SDK, 8.0.401 or a later 8.0 release. The templates pin it in `global.json`, and the
  packages target `net8.0`.
- The .NET 11 preview SDK, only for the `union` response model.
- The Smithy CLI on `PATH`, only for a Smithy contract.

## Quick start

```bash
dotnet new install Hardened.Templates
dotnet new hardened-web -n Todos
cd Todos
dotnet run --project src/Todos.Host
```

```console
$ curl localhost:5080/todos/1
{"id":1,"title":"Read the generated code","done":true}
```

The `hardened-web` template creates four projects:

| Project | Contents |
|---|---|
| `src/Todos` | The handlers, the models and the services |
| `src/Todos.Host` | The application class and `Program.cs` for the chosen host |
| `src/Todos.Client` | A Kiota client generated from the OpenAPI document |
| `tests/Todos.Tests` | Tests that send requests through the application in process |

The application serves its OpenAPI document at `/openapi.json`. In the `development` environment it
also serves a reference page at `/docs`. The environment is `development` when
`HARDENED_ENVIRONMENT` is not set. `dotnet test` runs the tests.

Template options select the host, the contract style, the response model, the client and the test
libraries. [Project templates](#project-templates) lists them.

## Handlers

A route attribute on a method makes the method a handler. The class does not need a base type, an
interface or a registration.

```csharp
public class TodoController
{
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

The generator writes the binding code for this signature. The name `id` matches the `{id}` path
token, so `id` binds from the path. `ITodoStore` is a registered service, so `store` comes from the
container. A parameter that matches neither binds from the request body. Attributes such as
`[FromQueryString]`, `[FromHeader]` and `[FromForm]` select other sources. A parameter that cannot
be bound fails the build.

The return type declares two outcomes. The method returns a `Todo` for a 200 or a `NotFound` for a
404, and the OpenAPI document describes both. [Responses](#responses) describes the other ways to
declare them.

`[Range(Min = 1)]` is a constraint attribute. `Hardened.Validation.SourceGenerator` turns it into a
check that runs before the handler. A request that fails the check gets a 400 that names the field
and the rule:

```console
$ curl localhost:5080/todos/0
{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"id","code":"range","message":"id must be at least 1."}]}
```

## Modules

A `partial` class marked `[HardenedModule]` is a module. The generator writes the other half of the
class, including `PopulateServiceCollection`. It also writes an attribute class for the module, such
as `TodosLibraryAttribute`. Another module imports this one by applying `[TodosLibrary]`.

The library module in `src/Todos` holds the routes:

```csharp
[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary;
```

`[BasePath]` on the module prefixes every route in its assembly.
`[Enable<OpenApiDocumentPublishing>]` serves the OpenAPI document that the build writes from the
routes in the same assembly. Put it on the module that holds the routes. On a module with no routes,
it serves a document with no paths.

The application module in `src/Todos.Host` names the host and imports the library:

```csharp
[HardenedModule]
[KestrelRuntime]
[TodosLibrary]
public partial class Application;
```

`Program.cs` fills a `ServiceCollection` from the application and starts the host:

```csharp
var services = new ServiceCollection();

services.AddLogging(logging => logging.AddSimpleConsole());
services.AddHardenedEnvironment(args);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services,
    kestrel => kestrel.ListenAnyIP(5080)
);

await app.RunAsync();
```

The templates set `EmitCompilerGeneratedFiles`. After a build, the generated C# is in
`obj/<configuration>/<tfm>/generated/`, with one directory for each generator.

## Hosts

The application module names the host with an attribute from the host's package. Handlers, filters
and binding do not change with the host.

| Host | Attribute | Package |
|---|---|---|
| Kestrel, without the ASP.NET Core pipeline | `[KestrelRuntime]` | `Hardened.Web.Kestrel.Runtime` |
| ASP.NET Core, through `app.UseHardened()` | `[AspNetCoreRuntime]` | `Hardened.Web.AspNetCore.Runtime` |
| AWS Lambda, behind an API Gateway HTTP API or a function URL | `[LambdaHttpModule]` | `Hardened.Aws.Lambda.Http` |
| Google Cloud Run | `[CloudRunRuntime]` | `Hardened.Gcp.CloudRun.Runtime` |
| Azure Functions, isolated worker | `[HttpModule]` | `Hardened.Azure.Functions.Http` |

`--host` on the `hardened-web` template selects one. Only the host project changes.
`Hardened.Gcp.Functions.Runtime` runs the same `[CloudRunRuntime]` application as a Google Cloud
Functions (2nd gen) function.

## Functions

A function handler names its event source with an attribute from `Hardened.Functions.Runtime`. The
attribute names a queue, a topic, a schedule, a table, a stream or a bucket. It does not name a
cloud.

```csharp
public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

The adapter package that the project references decides what delivers to the handler. With
`Hardened.Aws.Lambda.Sqs`, the method receives SQS messages. With
`Hardened.Azure.Functions.ServiceBus`, the same method receives Service Bus messages. Each adapter
package sets a build property that the generator reads, so the application does not name the
adapter.

| Attribute | AWS Lambda | Google Cloud Run | Azure Functions |
|---|---|---|---|
| `[HardenedFunction]` | Direct invocation | Direct invocation over HTTP | Not available |
| `[Queue]` | SQS | Pub/Sub push subscription | Service Bus queue |
| `[Topic]` | SNS | Pub/Sub, through Eventarc | Service Bus topic |
| `[Timer]` | EventBridge | Cloud Scheduler | Timer trigger |
| `[Event]` | EventBridge | Eventarc | Event Grid |
| `[Change]` | DynamoDB Streams | Firestore | Cosmos DB |
| `[Stream]` | Kinesis | Not available | Event Hubs |
| `[Blob]` | S3 | Cloud Storage | Blob Storage, through Event Grid |

One adapter package serves each entry. The
[package reference](https://ipjohnson.github.io/Hardened.Framework/reference/packages) lists them. A
trigger with no adapter on the chosen cloud fails the build with `HRDF001`.

A function runs through the same pipeline as an HTTP request, so validation and filters apply to
it. The runtime splits a batch into one call for each item. The `hardened-function` template
writes a function and its tests for one trigger and one cloud.

## Contracts

The API contract is C# code, an OpenAPI document or a Smithy model. `--contract` on the
`hardened-web` template selects `code`, `openapi` or `smithy`.

In a code-first project the handlers are the contract. The build writes the OpenAPI document from
the routes.

In an OpenAPI-first project the document is an item in the project file:

```xml
<ItemGroup>
  <HardenedOpenApiSpec Include="contracts\todos.yaml">
    <PublishUrl>/openapi.json</PublishUrl>
  </HardenedOpenApiSpec>
</ItemGroup>
```

The build generates a model for each schema, a service interface for each tag, the routes, and the
validation for the schema constraints. The application implements the interface on a class marked
`[Handler]`:

```csharp
[Handler]
public class TodoService(ITodoStore store) : ITodosService
{
    public async Task<GetTodoResponse> GetTodo(int id)
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

`GetTodoResponse` has one case for each status that the operation declares. When the contract gets
a new operation, the build fails until the class implements the new method.

A Smithy-first project declares its model as a `HardenedSmithyModel` item. The build generates the
same kinds of interfaces and models that it generates from an OpenAPI document. The build runs the
Smithy CLI, and it warns with `HSMT011` when the installed version is not the pinned version.

For every contract style, `<HardenedOpenApiOutput>` writes the served document to a file after each
compile. The document is OpenAPI 3.2. `<HardenedOpenApiOutputVersion>` writes a 3.0.0 or 3.1.0
file instead, for tools that cannot read 3.2. The `hardened-web` template generates its client from
this file.

## Responses

A handler declares its responses in one of three ways.

| Model | Return type | Other statuses | Requires |
|---|---|---|---|
| Response | `Response<T1..Tn>` with every case | Returned | `net8.0` |
| Throws | The success type | Thrown, and declared with `[Throws<T>]` | `net8.0` |
| Union | A C# `union` of every case | Returned | `net11.0` and `<LangVersion>preview</LangVersion>` |

In a code-first project, the return type selects the model. In a contract-first project,
`<HardenedResponseModel>` selects the shape of the generated interface. A project that declares
nothing gets Throws. The templates use Response.

`Response<T1..Tn>` is a struct with an implicit conversion from each case, so a handler returns the
case value directly. The built-in cases are records that carry their status, for example
`Created<T>`, `NoContent`, `NotFound` and `Conflict`. Most error cases also have a `<T>` form that
carries a body type from the application.

In the Throws model the handler throws the case, and `[Throws<T>]` adds its status to the OpenAPI
document. Nothing checks that the handler throws only the declared statuses.

```csharp
[Get("/{id}")]
[Throws<NotFound>]
public async Task<Todo> ById(ITodoStore store, int id)
{
    var todo = await store.Find(id);

    if (todo is null)
    {
        throw new NotFound("todo", $"No todo has id {id}.").AsException();
    }

    return todo;
}
```

A union declares the same set of cases as `Response<T1..Tn>`. The handler body does not change.

```csharp
public union TodoResult(Todo, NotFound);
```

The compiler needs types from .NET 11 to compile a union. AWS Lambda and Azure Functions do not run
`net11.0`, so the template refuses `--response-model union` with those two hosts.

## Filters

Every request runs through a pipeline of filters. HTTP requests, function invocations and queue
messages use the same pipeline. The handler is the last filter. A filter runs its code around
`chain.Next()`. If a filter returns without calling `chain.Next()`, the filters after it and the
handler do not run.

```csharp
public class ServerTimingFilter : IExecutionFilter
{
    public async Task Execute(IExecutionChain chain)
    {
        var start = MachineTimestamp.Now;

        await chain.Next();

        chain.Context.Response.Headers["Server-Timing"] =
            $"app;dur={start.GetElapsedMilliseconds():0.0}";
    }
}
```

An attribute that implements `IRequestFilterProvider` attaches filters to the handler, the class or
the module that it is applied to:

```csharp
public class ServerTimingAttribute : Attribute, IRequestFilterProvider
{
    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo)
    {
        yield return new RequestFilterInfo(_ => new ServerTimingFilter(), FilterOrder.DefaultValue);
    }
}
```

`[Retry]` is a built-in attribute of this kind. `IGlobalFilterRegistry` attaches a filter to every
handler. `FilterOrder` places a filter relative to the built-in stages, which include
authentication, authorization, response caching, serialization and validation.

To see the filter chain of each handler, enable the `Hardened.Requests.Pipeline` log category at
`Debug`. Each chain is logged once, when it is built.

## Testing

`[HardenedTest]` builds the application for a test method and passes the method's parameters from
it. Requests go through routing, filters, binding, the handler and serialization in the test
process. No socket is opened.

```csharp
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(TodosLibrary))]
[assembly: KiotaTesting]
[assembly: NSubstituteSupport]

public class TodoTests
{
    [HardenedTest]
    public async Task GetTodo_ReturnsTheStoredTodo(TodosClient client, [Mock] ITodoStore store)
    {
        store.Find(1).Returns(new Todo(1, "Write the README", false));

        var todo = await client.Todos[1].GetAsync().Returns<Ok<ClientModels.Todo>>();

        Assert.Equal("Write the README", todo.Value.Title);
    }

    [HardenedTest]
    public async Task GetTodo_UnknownId_IsNotFound(TodosClient client)
    {
        var missing = await client.Todos[9999].GetAsync().Returns<NotFound<ClientModels.NotFound>>();

        Assert.Contains("9999", missing.Body.Detail);
    }

    [HardenedTest]
    public async Task GetTodo_MalformedId_IsBadRequest(ITestWebApp app)
    {
        (await app.Get("/todos/not-a-number")).Assert.BadRequest();
    }
}
```

- `[assembly: WebTesting]` provides `ITestWebApp` and the test credentials.
- `[assembly: HardenedTestEntryPoint]` names the module under test.
- `[assembly: KiotaTesting]` makes a Kiota client a test parameter. The client sends its requests
  into the pipeline.
- `Returns<T>()` asserts that a call answered with the declared response `T`. It checks the
  status, the body type and the headers of that response.
- `[Mock]` replaces a service in the application's container, so the handler uses the mock that the
  test configures. `[assembly: NSubstituteSupport]` selects NSubstitute. `MoqSupport` and
  `FakeItEasySupport` select the other two libraries.
- `ITestWebApp` sends a raw request, such as a malformed path that the typed client cannot send.
- `[Grants]`, `[Subject]` and `[Anonymous]` set the caller's credentials on a parameter, a method,
  a class or the assembly.

A Refit client works the same way with `Hardened.Refit.Testing` and `[assembly: RefitTesting]`.
`[HardenedTest]` is in `Hardened.Shared.Testing.xUnit` for xUnit v3 and in
`Hardened.Shared.Testing.NUnit` for NUnit.

Function tests use `[assembly: FunctionTesting]`, which delivers straight into the pipeline. The
testing package of each cloud delivers through that provider's real event envelope instead:
`[assembly: LambdaTesting]`, `[assembly: CloudRunTesting]` and `[assembly: AzureFunctionsTesting]`.

## HTML views

`[Output<T>]` renders the return value of a handler with a view instead of a serializer. A view is a
`.cshtml` file that [RazorBlade](https://github.com/ltrzesniewski/RazorBlade) compiles to C# during
the build.

```csharp
[Get("/orders")]
[Output<Views.Orders>]
public OrderListModel List() => _orders.Recent();
```

```razor
@inherits Contoso.Orders.ApplicationRazorTemplates<OrderListModel>

<table>
@foreach (var order in Model.Orders)
{
    <tr><td>@order.Reference</td><td>@order.Total</td></tr>
}
</table>
```

Views need two package references and one attribute:

1. Reference both `Hardened.Templates.RazorBlade` and `RazorBlade`. RazorBlade's build props do not
   flow through the Hardened package. Without the direct reference, no view is compiled and the
   build reports no error.
2. Add `[Enable<RazorTemplates>]` to the application module. The generator writes
   `ApplicationRazorTemplates<TModel>`, which is the base class that a view names in `@inherits`.

A view that uses a model member that does not exist fails the build. A route with `[Output<T>]`
renders the view for every `Accept` header, so the route never sends the model as JSON.

## Native AOT

The runtime packages set `IsAotCompatible`, so the build runs the trim and AOT analyzers on them.
CI publishes a Kestrel application, a Lambda function and a Cloud Run service with Native AOT and
with warnings as errors. CI then runs each native binary and checks its answer to a request.

The Azure Functions worker cannot start as a native binary
([azure-functions-dotnet-worker#1056](https://github.com/Azure/azure-functions-dotnet-worker/issues/1056)),
so CI runs a trimmed build of it instead.

Register the application's `JsonSerializerContext` as an `IJsonTypeInfoResolver`. The JSON
serializers do not use a context that is not registered, and nothing reports it. The `hardened-web`
template registers `TodosJsonContext` in `TodosLibrary`.

## Other features

| Guide | Summary |
|---|---|
| [Services](https://ipjohnson.github.io/Hardened.Framework/guide/services) | A lifetime attribute on a class, such as `[SingletonService]`, registers the class |
| [Configuration](https://ipjohnson.github.io/Hardened.Framework/guide/configuration) | The generator turns a `[ConfigurationModel]` partial class of private fields into an interface, properties and environment variable reads |
| [Authentication](https://ipjohnson.github.io/Hardened.Framework/guide/authentication) | A handler names the scheme it requires. A request without credentials is refused before the handler runs |
| [Authorization](https://ipjohnson.github.io/Hardened.Framework/guide/authorization) | `[AuthorizeGrants]` names the grants that an operation requires |
| [Rate limiting](https://ipjohnson.github.io/Hardened.Framework/guide/rate-limiting) | `[RateLimit]` limits how often a handler can be called |
| [Request timeouts](https://ipjohnson.github.io/Hardened.Framework/guide/request-timeouts) | `[Timeout]` gives the handler a `CancellationToken` for a time budget. A handler that observes the token answers 504 when the budget runs out |
| [Response caching](https://ipjohnson.github.io/Hardened.Framework/guide/response-caching) | `[CacheResponse<T>]` stores a response. A later request with the same key gets the stored response, and the handler does not run |
| [Conditional requests](https://ipjohnson.github.io/Hardened.Framework/guide/conditional-requests) | `[ConditionalGet]` answers 304 to a client that already has the current response |
| [Compression](https://ipjohnson.github.io/Hardened.Framework/guide/compression) | Responses are compressed when the application enables it. Compressed request bodies are decoded in every application |
| [Streaming](https://ipjohnson.github.io/Hardened.Framework/guide/streaming) | An `IAsyncEnumerable<T>` return value streams as NDJSON, or as server-sent events with `[ServerSentEvents]` |
| [Content negotiation](https://ipjohnson.github.io/Hardened.Framework/guide/content-negotiation) | An operation declares the media types it produces, and the `Accept` header selects the serializer |
| [MessagePack](https://ipjohnson.github.io/Hardened.Framework/guide/message-pack) | `Hardened.Requests.Serializers.MessagePack` adds MessagePack as a second representation beside JSON |

## Project templates

`dotnet new install Hardened.Templates` installs three templates. In each table, the first value of
an option is its default.

### hardened-web

An HTTP API: a library for the handlers, a host project, a client project and tests.

| Option | Values | Selects |
|---|---|---|
| `--host` | `kestrel`, `aspnet`, `aws-lambda`, `cloud-run`, `azure-functions` | Where the application runs. Only the host project changes |
| `--contract` | `code`, `openapi`, `smithy` | The contract style. See [Contracts](#contracts) |
| `--response-model` | `response`, `throws`, `union` | How a handler declares its statuses. See [Responses](#responses) |
| `--client` | `kiota`, `refit`, `none` | The generated client, and the tests that use it |
| `--serializer` | `json`, `message-pack-named`, `message-pack-keyed` | MessagePack as a second representation beside JSON |
| `--openapi-ui` | `true`, `false` | Whether `/docs` serves a reference page in development |

### hardened-function

A function for one trigger, and tests. The function project is the deployed artifact, so there is no
separate host project.

| Option | Values | Selects |
|---|---|---|
| `--trigger` | `invoke`, `queue`, `topic`, `timer`, `change`, `stream`, `blob` | The trigger attribute on the handler |
| `--host` | `aws`, `gcp`, `azure` | The cloud. It decides the package references and the entry point |

### hardened-library

A module that an application imports with one attribute, and tests. It has only the options in the
next table.

### Options on every template

| Option | Values | Selects |
|---|---|---|
| `--test-framework` | `xunit`, `nunit` | The test framework |
| `--mocks` | `nsubstitute`, `moq`, `fakeiteasy` | The library that supplies `[Mock]` parameters |
| `--hardened-version` | A package version | The Hardened version to pin. The default is the version of the template |

The templates refuse three combinations: `--response-model union` with `--host aws-lambda` or
`--host azure-functions`, and `--contract smithy` with a MessagePack serializer.

## Packages

Every package is on nuget.org. All packages release together on one version line, and a release
version has the form `0.N.0-rc1000`. Use the same version for every Hardened package in a solution,
because the generated code and the runtime that it calls ship together.

The templates reference the packages that each project needs. A project assembled by hand needs the
runtime packages and the source generator packages. Analyzers do not flow through a package
reference, so the project that declares the application must reference the generators directly.
Without `Hardened.Web.SourceGenerator`, the application compiles and answers 404 to every request.

A code-first Kestrel application references these packages:

| Package | Contents |
|---|---|
| `Hardened.Shared.Runtime` | Modules, configuration and the environment |
| `Hardened.Web.Runtime` | Routing, the OpenAPI document and the reference page |
| `Hardened.Web.Kestrel.Runtime` | `[KestrelRuntime]` and `HardenedKestrelApplication` |
| `Hardened.Library.SourceGenerator` | The generator for modules, service registration and configuration |
| `Hardened.Web.SourceGenerator` | The generator for route tables and handlers |
| `Hardened.Validation.SourceGenerator` | The generator for validators from constraint attributes |

The [package reference](https://ipjohnson.github.io/Hardened.Framework/reference/packages) lists
every package.

## Building from source

`global.json` pins a .NET 11 preview SDK, because the repository contains C# `union` declarations.
Every project targets `net8.0`, and the tests run on the .NET 8 runtime. Install both.

```bash
dotnet build Hardened.slnx
dotnet test Hardened.slnx
```

CI builds with `--configuration Release -p:ContinuousIntegrationBuild=true`, which treats warnings
as errors. [AGENTS.md](https://github.com/ipjohnson/Hardened.Framework/blob/main/AGENTS.md)
describes the repository layout, the formatting rules and the CI checks.

## License

Hardened is licensed under the
[MIT license](https://github.com/ipjohnson/Hardened.Framework/blob/main/LICENSE).
