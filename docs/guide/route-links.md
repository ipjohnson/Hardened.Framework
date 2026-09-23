# Route links

The build generates two classes inside each module class. `Routes` builds the path of a route, and
`Links` builds a link that a client can call.

In a project made with `dotnet new hardened-web -n Todos`, the `Create` handler writes the path of a
new todo by hand, as `$"/todos/{todo.Id}"`. This excerpt of its `src/Todos/TodoController.cs`
builds the path with `TodosLibrary.Routes` instead:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Todos;

public class TodoController
{
    [Operation("createTodo")]
    [Post("/")]
    public async Task<Response<Created<Todo>, Conflict>> Create(ITodoStore store, NewTodo request)
    {
        if (await store.TitleExists(request.Title))
        {
            return new Conflict($"A todo titled '{request.Title}' already exists.");
        }

        var todo = await store.Add(request.Title);

        return new Created<Todo>(todo, TodosLibrary.Routes.Todo.ById(todo.Id));
    }
}
```

```http
POST /todos
Content-Type: application/json

{"title":"Write the docs"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3

{"id":3,"title":"Write the docs","done":false}
```

`TodosLibrary` is the project's library module. `TodosLibrary.Routes.Todo.ById(3)` returns
`/todos/3`. That path is the route of `TodoController.ById`, `GET /todos/{id}`, with its token
filled in. Each member is named after its controller and handler method, so renaming either one
renames the member.

## The generated classes

The build generates both classes for every `[HardenedModule]` class in a project that references
`Hardened.Web.SourceGenerator`. A module with no routes gets them too. The template's `Application`
gets an empty `Routes`.

The classes for `TodosLibrary` are in `TodosLibrary.Links.cs`, among the generated sources.
[From scratch](/guide/from-scratch) shows where the generated sources are. The file declares these
members for the template's four routes:

| Route | Handler | `Routes` member | `Links` members |
|---|---|---|---|
| `GET /todos` | `TodoController.All` | `Routes.Todo.All()` | `Links.Todo.All()`, `Links.Todo.AllAbsolute()` |
| `POST /todos` | `TodoController.Create` | `Routes.Todo.Create()` | `Links.Todo.Create()`, `Links.Todo.CreateAbsolute()` |
| `GET /todos/{id}` | `TodoController.ById` | `Routes.Todo.ById(int id)` | `Links.Todo.ById(int id)`, `Links.Todo.ByIdAbsolute(int id)` |
| `DELETE /todos/{id}` | `TodoController.Remove` | `Routes.Todo.Remove(int id)` | `Links.Todo.Remove(int id)`, `Links.Todo.RemoveAbsolute(int id)` |

`Routes` is a static class. It holds a static class for each group, such as `Todo`. Each route is a
static method that returns the path as a `string`. The path includes the `[BasePath]` of the module
and of the controller.

`Links` is a sealed class. Its constructor takes an `ILinkContext`. It has a property for each
group. Each group has two methods for each route, and both return a `string`. One has the name of
the `Routes` member, and the other adds `Absolute` to that name.
[Links a client can call](#links-a-client-can-call) covers what each one returns.

## Member names

The build names each member after the handler and its controller:

| Part | Comes from | Example |
|---|---|---|
| Group | The controller's class name, without a `Controller` suffix | `TodoController` gives `Todo` |
| Group, when the controller has `[Tag]` | The tag | `[Tag("Probes")]` gives `Probes` |
| Method | The handler method's name | `TodoController.ById` gives `ById` |
| Method, when the handler method's name equals the group's name | The handler method's name, then the HTTP method | `Summary()` on `SummaryController` gives `Routes.Summary.SummaryGet()` |
| Parameter, for a token that a handler parameter binds | The name and type of that handler parameter | `ById(int id)` |
| Parameter, for a token that no parameter binds | A `string` named after the token | `[Get("/probe/unbound/{code}")]` on a method with no parameters gives `Unbound(string code)` |

`[Tag]` is in `Hardened.Web.Runtime.Attributes`. The build makes a tag that is not a C# identifier
into one. It starts each run of letters and digits with an upper-case letter and drops every other
character, so `pet store` gives `PetStore`. It puts an underscore before a leading digit.

Two handlers can produce members with the same group, name and parameter types. The handler whose
full route sorts first by ordinal comparison gets the members, and the other handler gets none. The
build reports nothing. A controller with `[Tag("Todo")]` and a `ById(int id)` handler at
`/todos/legacy/{id}` takes `Routes.Todo.ById` from `TodoController.ById`. The member then returns
`/todos/legacy/42` in place of `/todos/42`.

::: warning
A second handler whose members collide can take the generated link without a message. The link
then points at a different route.
:::

## Values in the path

A generated method escapes a `string` for a `{name}` token with `Uri.EscapeDataString`. It writes a
`string` for a catch-all token as it is, `/` included. It writes a value of any other type with
`Convert.ToString(value, CultureInfo.InvariantCulture)`.

| Parameter | Value passed | Written into the path |
|---|---|---|
| `string name`, for `{name}` | `"a/b c"` | `a%2Fb%20c` |
| `string path`, for `{*path}` | `"a/b/c.png"` | `a/b/c.png` |
| `int id` | `42` | `42` |
| `decimal value` | `-4.5m` | `-4.5` |

On the Kestrel host, a handler that receives the link for `a/b c` binds `a%2Fb c`. The host decodes
`%20` and leaves `%2F` as it is. [Routing](/guide/routing) covers token values.

A `DateOnly` is written as `09/23/2026`, which a `{day:date}` token does not match.
[Limits](#limits) covers this case.

## Links a client can call

The generated code registers `TodosLibrary.Links` in the container as a transient service. A
controller takes it in its constructor, as this version of the excerpt does:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Todos;

public class TodoController(TodosLibrary.Links links)
{
    [Operation("createTodo")]
    [Post("/")]
    public async Task<Response<Created<Todo>, Conflict>> Create(ITodoStore store, NewTodo request)
    {
        if (await store.TitleExists(request.Title))
        {
            return new Conflict($"A todo titled '{request.Title}' already exists.");
        }

        var todo = await store.Add(request.Title);

        return new Created<Todo>(todo, links.Todo.ById(todo.Id));
    }
}
```

The build cannot resolve the links class as a parameter of a handler method. It skips that handler
and reports warning `HOAG010`:

```text
'ParamProbeController.P' was not generated because the type of parameter 'links' could not be resolved. Other handlers in this assembly are unaffected.
```

The build does the same when the parameter has `[FromServices]`.

A `Links` method passes the path through the link context that the class was built with. With no
configuration, a `Links` method returns the same path as the `Routes` member, and so does its
`Absolute` form.

## Base path, scheme and host

The default link context reads `LinkConfiguration`, which is in `Hardened.Web.Runtime.Links`.

| Property | Default | Used by |
|---|---|---|
| `BasePath` | `""` | Both `Links` methods, before the path |
| `Scheme` | `null` | The `Absolute` method |
| `Host` | `null` | The `Absolute` method |

An `AppConfig` amendment sets these properties. [Configuration](/guide/configuration) covers
`AppConfig`. This example puts the amendment in the application module.
`IServiceCollectionConfiguration` on a module class adds registrations in code.
[Modules](/guide/modules) covers it. The file is `src/Todos.Host/ApplicationConfiguration.cs`, a
second file of the partial `Application` class:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Web.Runtime.Links;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Host;

public partial class Application : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        var config = new AppConfig();

        config.Amend(
            (LinkConfiguration links) =>
            {
                links.BasePath = "/prod";
                links.Scheme = "https";
                links.Host = "api.example.com";
            }
        );

        services.AddSingleton<IConfigurationPackage>(config);
    }
}
```

With this file and the controller above, `Create` answers with the base path in `Location`:

```http
POST /todos
Content-Type: application/json

{"title":"Write the docs"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /prod/todos/3

{"id":3,"title":"Write the docs","done":false}
```

With this configuration, the members return these values:

| Call | Returns |
|---|---|
| `links.Todo.ById(42)` | `/prod/todos/42` |
| `links.Todo.ByIdAbsolute(42)` | `https://api.example.com/prod/todos/42` |
| `TodosLibrary.Routes.Todo.ById(42)` | `/todos/42` |

`Routes` does not read the link configuration.

The default link context removes a trailing `/` from `BasePath`, so `/prod/` builds the same links
as `/prod`. The `Absolute` method needs both `Scheme` and `Host`. With either one unset, it returns
the same link as the plain method.

On AWS Lambda behind an API Gateway stage such as `prod`, the adapter removes `/prod` from the path
before routing. A `BasePath` of `/prod` puts the stage back into the links.

## A custom link context

An application can register its own `ILinkContext`. `Links` then uses it in place of the default.
The interface has `BasePath`, `Scheme`, `Host`, `Resolve(path)` and `Absolute(path)`. A plain
`Links` method calls `Resolve`, and an `Absolute` method calls `Absolute`.

This version of `src/Todos.Host/ApplicationConfiguration.cs` registers one in place of the file
above:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Web.Runtime.Links;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Host;

public partial class Application : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ILinkContext, StageLinkContext>();
    }
}

public class StageLinkContext : ILinkContext
{
    public string BasePath => "/stage";

    public string? Scheme => "https";

    public string? Host => "stage.example.com";

    public string Resolve(string path) => BasePath + path;

    public string Absolute(string path) => Scheme + "://" + Host + Resolve(path);
}
```

With it, `links.Todo.ById(42)` returns `/stage/todos/42`. `links.Todo.ByIdAbsolute(42)` returns
`https://stage.example.com/stage/todos/42`.

## Links to an imported module

A module's `Links` has a property for each imported module that has links. The property is named
after that module. The template's `Application.Links` has a `TodosLibrary` property of type
`TodosLibrary.Links`. An imported module whose project has no generated `Links`, such as
`KestrelRuntime`, gets no property.

The host project's own generated code registers `Application.Links`. With an `Application.Links`
from the container, `links.TodosLibrary.Todo.ById(1)` returns `/todos/1`.

## Contract-first projects

A project generated from an OpenAPI contract gets the same two classes. The group is the
operation's tag. The method is the method of the generated interface.

In a project made with `dotnet new hardened-web -n Todos -c openapi`, the contract tags every
operation `Todos`. Its `TodosLibrary.Links.cs` declares `Routes.Todos.GetTodo(int id)` for the
operation `getTodo` at `/todos/{id}`. The `CreateTodo` method in its `src/Todos/TodoService.cs`
writes the `Location` with `TodosLibrary.Routes.Todos.GetTodo(created.Id)`.

A Smithy contract goes through the same generator.

## Limits

A route registered at startup has no member in `Routes` or `Links`. The build writes both classes
from the routes it can see. [Registered routes](/guide/registered-routes) covers routes registered
at startup.

A `DateOnly` token value is written in the invariant culture's short date format. Its slashes make
a path that the route does not match, so a request to the link gets a 404.

## Next

| Page | Covers |
|---|---|
| [Routing](/guide/routing) | The routes these members are built from |
| [Configuration](/guide/configuration) | `AppConfig` and amending a configuration type |
| [Declared responses](/guide/responses) | `Created<T>` and the other responses that carry a `Location` |
| [Registered routes](/guide/registered-routes) | Routes whose paths are computed at startup |
