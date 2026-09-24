# Views

`[Output<T>]` on a handler names a view, and the view renders the handler's return value as HTML. A
view is a `.cshtml` file that RazorBlade compiles into a C# class at build time.

This view is `src/Todos/Views/TodoList.cshtml`:

```razor
@inherits TodosLibraryRazorTemplates<IReadOnlyList<Todo>>
<ul>
    @foreach (var todo in Model)
    {
        <li>@todo.Title</li>
    }
</ul>
```

The handler in `src/Todos/PageController.cs` names it:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class PageController
{
    [Get("/page")]
    [Output<Views.TodoList>]
    public Task<IReadOnlyList<Todo>> List(ITodoStore store) => store.All();
}
```

```http
GET /todos/page

HTTP/1.1 200 OK
Content-Type: text/html; charset=utf-8

<ul>
        <li>Read the generated code</li>
        <li>Add an endpoint</li>
</ul>
```

The examples on this page are in the library project `src/Todos` of an application made with
`dotnet new hardened-web -n Todos`. The library module's `[BasePath("/todos")]` puts their routes
under `/todos`.

## Installing

The project that holds the views references two packages, `RazorBlade` and
`Hardened.Templates.RazorBlade`. The framework is built against RazorBlade 1.0.0.

RazorBlade's build files collect the `.cshtml` files. Those build files and RazorBlade's source
generator do not flow through `Hardened.Templates.RazorBlade`, so `RazorBlade` needs its own
reference. [Naming the view](#naming-the-view) gives the build error when that reference is
missing.

The template's `Directory.Packages.props` defines `$(HardenedVersion)`.
[Project templates](/guide/project-templates) covers the file. The file holds the versions:

```xml
<PackageVersion Include="RazorBlade" Version="1.0.0" />
<PackageVersion Include="Hardened.Templates.RazorBlade" Version="$(HardenedVersion)" />
```

`src/Todos/Todos.csproj` holds the references:

```xml
<PackageReference Include="RazorBlade" />
<PackageReference Include="Hardened.Templates.RazorBlade" />
```

`[Enable<RazorTemplates>]` on the module in the same project generates the base class that the
views inherit. `RazorTemplates` is in `Hardened.Templates.RazorBlade`. `Enable<T>` is in
`Hardened.Shared.Runtime.Attributes`. The base class is named after the module, in the module's
namespace. `TodosLibrary` gets `TodosLibraryRazorTemplates<TModel>`.

This is the scaffold's `src/Todos/TodosLibrary.cs` with the attribute and its `using` line added:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Templates.RazorBlade;
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
[Enable<RazorTemplates>]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }
}
```

A view needs no registration. The generated handler constructs the view for each response.

## Writing a view

`@inherits TodosLibraryRazorTemplates<TModel>` names the model type. `Model` is the value that the
handler returned.

RazorBlade makes each view an `internal` class. The class's namespace is made of the project's root
namespace and the file's folder, so `src/Todos/Views/TodoList.cshtml` becomes
`Todos.Views.TodoList`. A view has to be in the project whose handlers name it.

`@expression` writes HTML-encoded text. `@Html.Raw(value)` writes the value as it is.

A view compiles as C#. A member that the model lacks fails the build. The error names the line and
column in the `.cshtml` file:

```text
src/Todos/Views/TodoList.cshtml(5,55): error CS1061: 'Todo' does not contain a definition for 'Name'
```

## Naming the view

`[Output<T>]` is in `Hardened.Requests.Abstract.Attributes`. It takes the view's class. The name in
the attribute has to resolve from the handler's own namespace, or be fully qualified. The first
example names `Views.TodoList` from namespace `Todos`.

The build refuses each of these mistakes:

| Mistake | Build result |
|---|---|
| The view's model is not the handler's return type | `CS0266` in the generated handler: "Cannot implicitly convert type 'Todos.Views.TodoDetail' to 'Hardened.Requests.Abstract.Outputs.IHardenedResponseOutput<System.Collections.Generic.IReadOnlyList<Todos.Todo>>'" |
| `[Output<T>]` names a type that is not an output | `CS0311` on the attribute |
| `[Output<T>]` names a type with no public parameterless constructor | `CS0310` on the attribute |
| The view is named through a `using` | `CS0246` in the generated handler |
| The project does not reference `RazorBlade` | `CS0246` on the attribute: "The type or namespace name 'Views' could not be found" |

## The response

A handler with `[Output<T>]` answers with the view whatever the request's `Accept` says. The request
is not refused with 406. The model is never sent as JSON.

```http
GET /todos/page
Accept: application/json

HTTP/1.1 200 OK
Content-Type: text/html; charset=utf-8

<ul>
        <li>Read the generated code</li>
        <li>Add an endpoint</li>
</ul>
```

The view sets `Content-Type: text/html; charset=utf-8`, unless the handler has already set a content
type. [Content negotiation](/guide/content-negotiation) covers how every other handler's media type
is chosen.

The OpenAPI document publishes the success as `text/html; charset=utf-8` with a string schema, in
place of the model's schema. This excerpt of the served `/openapi.json` shows the responses of
`GET /todos/page`:

```json
"200": {
  "description": "OK",
  "content": {
    "text/html; charset=utf-8": {
      "schema": {
        "type": "string"
      }
    }
  }
}
```

## Other statuses

A handler with `[Output<T>]` cannot return `Response<Todo, NotFound>`. The build checks the whole
return type against the view's model and fails with `CS0266`. The handler returns the model and
throws the other statuses.

This view is `src/Todos/Views/TodoDetail.cshtml`:

```razor
@inherits TodosLibraryRazorTemplates<Todo>
<h1>@Model.Title</h1>
```

A second handler in `PageController` names it. The file adds
`using Hardened.Web.Runtime.Responses;`:

```csharp
[Get("/page/{id}")]
[Output<Views.TodoDetail>]
[Throws<NotFound>]
public async Task<Todo> Detail(ITodoStore store, int id)
{
    var todo = await store.Find(id);

    if (todo is null)
    {
        throw new NotFound("todo", $"No todo has id {id}.").AsException();
    }

    return todo;
}
```

A handler that throws is answered like any other handler. The view is not rendered. The error goes
out as JSON:

```http
GET /todos/page/9

HTTP/1.1 404 Not Found
Content-Type: application/json

{"resource":"todo","detail":"No todo has id 9.","type":"urn:hardened:problem:not-found","title":"Not Found","status":404}
```

`[Throws<NotFound>]` puts the 404 in the OpenAPI document. [Declared responses](/guide/responses)
covers `[Throws<T>]` and `AsException()`.

## Choosing a view per request

The generated handler sets `context.Response.OutputFactory` to the view in `[Output<T>]`, and then it
calls the method. The method can replace that view.

This second view over the same model is `src/Todos/Views/TodoCards.cshtml`:

```razor
@inherits TodosLibraryRazorTemplates<IReadOnlyList<Todo>>
@foreach (var todo in Model)
{
    <article>@todo.Title</article>
}
```

This `List` replaces the first example's `List` in `src/Todos/PageController.cs`. The file adds
`using Hardened.Requests.Abstract.Execution;` for `IExecutionContext`:

```csharp
[Get("/page")]
[Output<Views.TodoList>]
public Task<IReadOnlyList<Todo>> List(
    ITodoStore store,
    IExecutionContext context,
    [FromQueryString] string? layout
)
{
    if (layout == "cards")
    {
        context.Response.OutputFactory = static _ => new Views.TodoCards();
    }

    return store.All();
}
```

```http
GET /todos/page?layout=cards

HTTP/1.1 200 OK
Content-Type: text/html; charset=utf-8

    <article>Read the generated code</article>
    <article>Add an endpoint</article>
```

[The execution pipeline](/guide/execution-pipeline) covers the context's members.

The build does not check a view set this way. A view whose model does not match fails when it
renders, with an `InvalidOperationException`. The exception's message names both types. The route
answers 500 with an empty body.

## Links in a view

A view on the generated base class has a `Links` property. The property holds an instance of the
module's `Links` class, resolved from the request's services. [Route links](/guide/route-links)
covers the members of that class.

This version of `src/Todos/Views/TodoList.cshtml` puts a link on each title:

```razor
@inherits TodosLibraryRazorTemplates<IReadOnlyList<Todo>>
<ul>
    @foreach (var todo in Model)
    {
        <li><a href="@Links.Todo.ById(todo.Id)">@todo.Title</a></li>
    }
</ul>
```

```http
GET /todos/page

HTTP/1.1 200 OK
Content-Type: text/html; charset=utf-8

<ul>
        <li><a href="/todos/1">Read the generated code</a></li>
        <li><a href="/todos/2">Add an endpoint</a></li>
</ul>
```

A link to a route that does not exist fails the build at the view's line:

```text
src/Todos/Views/TodoList.cshtml(5,33): error CS1061: 'TodosLibrary.Links.TodoLinks' does not contain a definition for 'Find'
```

## Layouts and sections

A layout is a view that inherits `RazorBlade.HtmlLayout` and calls `@RenderBody()`. This layout is
`src/Todos/Views/Shell.cshtml`:

```razor
@inherits RazorBlade.HtmlLayout
<html>
<head><title>@RenderSection("title")</title></head>
<body>@RenderBody()</body>
</html>
```

A view names its layout by overriding `CreateLayout()` in an `@functions` block. `@section title { ... }`
in the view fills `@RenderSection("title")` in the layout. This is `src/Todos/Views/TodoList.cshtml`
on that layout:

```razor
@inherits TodosLibraryRazorTemplates<IReadOnlyList<Todo>>
@section title {Todos}
<ul>
    @foreach (var todo in Model)
    {
        <li>@todo.Title</li>
    }
</ul>
@functions {
    protected override RazorBlade.HtmlLayout? CreateLayout() => new Shell();
}
```

```http
GET /todos/page

HTTP/1.1 200 OK
Content-Type: text/html; charset=utf-8

<html>
<head><title>Todos</title></head>
<body><ul>
        <li>Read the generated code</li>
        <li>Add an endpoint</li>
</ul>
</body>
</html>
```

Layouts and sections are RazorBlade's. The
[RazorBlade documentation](https://github.com/ltrzesniewski/RazorBlade) covers the rest.

## Contract-first operations

In a project generated from an OpenAPI description or a Smithy model, an operation whose success is
`text/html` with an object or array schema needs a view. `[Output<T>]` goes on the method that
implements the operation.

This operation goes under `paths` in `src/Todos/contracts/todos.yaml`, in an application made with
`dotnet new hardened-web -n Todos -c openapi`:

```yaml
  /todos/page:
    get:
      tags:
        - Todos
      operationId: todoPage
      responses:
        '200':
          description: Every todo, as a page.
          content:
            text/html:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/Todo'
```

This method goes on `TodoService` in `src/Todos/TodoService.cs`. The file already has
`using Hardened.Requests.Abstract.Attributes;`:

```csharp
[Output<Views.TodoList>]
public async Task<List<Todo>> TodoPage() => (await store.All()).ToList();
```

Without `[Output<T>]` on the method, the build fails with `HOAG020`:

```text
'ITodosService.TodoPage' answers 'text/html' with a model, and declares no [Output<T>]. Nothing serializes an object as markup, so this would fail at run time. Name the view on the implementation.
```

A view over `IReadOnlyList<Todo>` serves a method that returns `List<Todo>`. The view imports the
generated models' namespace with `@using Todos.Models`. Its links are grouped by the contract's tag.
This is `src/Todos/Views/TodoList.cshtml` in that project:

```razor
@using Todos.Models
@inherits TodosLibraryRazorTemplates<IReadOnlyList<Todo>>
<ul>
    @foreach (var todo in Model)
    {
        <li><a href="@Links.Todos.GetTodo(todo.Id)">@todo.Title</a></li>
    }
</ul>
```

```http
GET /todos/page
Accept: application/json

HTTP/1.1 200 OK
Content-Type: text/html; charset=utf-8

<ul>
        <li><a href="/todos/1">Read the generated code</a></li>
        <li><a href="/todos/2">Add an endpoint</a></li>
</ul>
```

The served document keeps the contract's declaration for the operation: `text/html` with an array
of `Todo`. [Generating from OpenAPI](/guide/openapi) and [Generating from Smithy](/guide/smithy)
cover the generated service interface.

## Writing an output of your own

`[Output<T>]` takes any class that implements `IHardenedResponseOutput` and has a public
parameterless constructor. The build checks the model against `IHardenedResponseOutput<TModel>`.
Both interfaces are in `Hardened.Requests.Abstract.Outputs`.

`WriteOutput` writes the whole response, its content type included. The output reads the handler's
return value from `context.Response.ResponseValue`.

This output is `src/Todos/TodoCsv.cs`:

```csharp
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Outputs;

namespace Todos;

public class TodoCsv : IHardenedResponseOutput<IReadOnlyList<Todo>>
{
    public async Task WriteOutput(IExecutionContext context)
    {
        var todos = (IReadOnlyList<Todo>)context.Response.ResponseValue!;

        context.Response.ContentType = "text/csv";

        await using var writer = new StreamWriter(context.Response.Body, leaveOpen: true);

        foreach (var todo in todos)
        {
            await writer.WriteLineAsync($"{todo.Id},{todo.Title},{todo.Done}");
        }
    }
}
```

The handler in `src/Todos/ExportController.cs` names it:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class ExportController
{
    [Get("/export")]
    [Output<TodoCsv>]
    public Task<IReadOnlyList<Todo>> Export(ITodoStore store) => store.All();
}
```

```http
GET /todos/export

HTTP/1.1 200 OK
Content-Type: text/csv

1,Read the generated code,True
2,Add an endpoint,False
```

## Hosts

Views render on the Kestrel, ASP.NET Core and AWS Lambda hosts, and in Native AOT binaries. A view
is written in the request pipeline that every host shares, so the other hosts render views the same
way. [JSON serialization](/guide/json) covers publishing with Native AOT.

## Limits

The OpenAPI document publishes the success of an output of your own as `text/html; charset=utf-8`
with a string schema, whatever the output writes.

With `[Produces("text/csv")]` on `ExportController.Export`, the document publishes `text/csv` with
the model's schema, an array of `Todo`. The build warns `HRDR012`:

```text
'ExportController.Export' declares [Produces("text/csv")] and returns a model, and nothing here writes a model as that media type.
```

The response does not change.

RazorBlade warns `RB0006` in a project on the Razor SDK. `Microsoft.NET.Sdk.Web` also uses the
Razor SDK. The Razor SDK's generator reads the same `.cshtml` files as RazorBlade. The template's
projects use `Microsoft.NET.Sdk`.

## Next

| Page | Covers |
|---|---|
| [Content negotiation](/guide/content-negotiation) | How a handler without a view chooses its media type |
| [Route links](/guide/route-links) | The `Links` members that a view uses |
| [Declared responses](/guide/responses) | `[Throws<T>]` and the built-in responses |
| [Generating from OpenAPI](/guide/openapi) | Generating the service interface that a handler with a view implements |
