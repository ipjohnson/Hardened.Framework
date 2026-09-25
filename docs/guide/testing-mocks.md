# Substituting services

`[Mock]` on a test parameter registers a test double for the parameter's type in the test's
container, in place of the application's own registration. The test receives the same double that
the application's code resolves.

```csharp
using DependencyModules.Testing.Attributes;
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;
using NSubstitute;
using Xunit;

namespace Todos.Tests;

public class TodoMockTests
{
    [ModuleTest]
    public async Task GetTodo_ReadsTheMock(ITestWebApp app, [Mock] ITodoStore store)
    {
        store.Find(1).Returns(new Todo(1, "from the mock", false));

        var response = await app.Get("/todos/1");

        Assert.Equal("from the mock", response.Deserialize<Todo>().Title);
        await store.Received().Find(1);
    }
}
```

The handler calls the double that the test set up. The test can check those calls afterwards.

The examples are tests in `tests/Todos.Tests`, the test project of `dotnet new hardened-web -n Todos`.
`ITodoStore` is the template's store, which starts with two todos, ids 1 and 2.

`[Mock]` is `MockAttribute` in `DependencyModules.Testing.Attributes`. `Hardened.Shared.Testing`
brings it through its dependency on `DependencyModules.Testing`.

The runner registers `[Mock]` parameters after the application's modules and after every test
registration attribute, so the mock replaces both.

## Mock libraries

`[Mock]` asks a mock library for the double. A support attribute names the library.

| Library | Package | Attribute | Namespace |
|---|---|---|---|
| NSubstitute | `DependencyModules.NSubstitute` | `[NSubstituteSupport]` | `DependencyModules.NSubstitute` |
| Moq | `DependencyModules.Moq` | `[MoqSupport]` | `DependencyModules.Moq` |
| FakeItEasy | `DependencyModules.FakeItEasy` | `[FakeItEasySupport]` | `DependencyModules.FakeItEasy` |

The support attribute goes on a method, a class or the assembly. The template puts it on the
assembly in `Bootstrap.cs`, which [Writing a test](/guide/testing) covers. `--mocks` chooses the
library when the template writes the project. [Project templates](/guide/project-templates) covers
the option.

Without a support attribute in scope, a test with a `[Mock]` parameter fails with
`System.Exception`: "Mock library not found, please ensure the Type or Assembly is attributed
correctly."

With FakeItEasy, the test sets up the parameter with `A.CallTo(() => store.Find(1)).Returns(...)`.

A mock replaces the whole service, so a member that the test does not set up returns the
library's default. With an unconfigured `[Mock] ITodoStore`, `GET /todos/2` answers 404. Moq's mocks are
loose, so an unconfigured member returns the default under Moq too.

## Moq's `Mock<T>`

Under `[MoqSupport]`, a parameter typed `Mock<ITodoStore>` receives the Moq mock. The container
resolves `ITodoStore` to the mock's `Object`. The parameter needs no `[Mock]`.

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;
using Moq;
using Xunit;

namespace Todos.Tests;

public class TodoMoqTests
{
    [ModuleTest]
    public async Task GetTodo_ReadsTheMock(ITestWebApp app, Mock<ITodoStore> store)
    {
        store.Setup(s => s.Find(1)).ReturnsAsync(new Todo(1, "from the mock", false));

        var response = await app.Get("/todos/1");

        Assert.Equal("from the mock", response.Deserialize<Todo>().Title);
        store.Verify(s => s.Find(1), Times.Once());
    }
}
```

A parameter typed `ITodoStore` and marked `[Mock]` receives the `Object` instead. `Mock.Get(store)`
returns the mock behind it. A test that takes both parameters gets one mock. Its
`[Mock] ITodoStore` is the `Object` of its `Mock<ITodoStore>`.

## A mock across requests

A `[Mock]` is one object for the whole test. Every container that the test builds gets it,
including the container each request runs in on the pipeline host.
[Writing a test](/guide/testing) covers the container per request and which hosts build one.

A second method of `TodoMockTests` sends two requests to one mock:

```csharp
    [ModuleTest]
    public async Task EveryRequestReachesTheSameMock(ITestWebApp app, [Mock] ITodoStore store)
    {
        store.Find(1).Returns(new Todo(1, "from the mock", false));

        await app.Get("/todos/1");
        await app.Get("/todos/1");

        await store.Received(2).Find(1);
    }
```

The mock reaches the handler both through `ITestWebApp` and through a generated client.

A parameter marked both `[Mock]` and `[FromKeyedServices("archive")]` replaces the keyed
registration. The unkeyed registration stays in place.

## A class of your own

`[TestExport]` registers a class of your own for every test it covers. The attribute is
`TestExportAttribute` in `DependencyModules.Testing.Attributes`. It goes on a method, a class or
the assembly. Several can apply to one test.

`EmptyTodoStore` implements `ITodoStore` in the test project:

```csharp
namespace Todos.Tests;

public sealed class EmptyTodoStore : ITodoStore
{
    public Task<IReadOnlyList<Todo>> All() => Task.FromResult<IReadOnlyList<Todo>>([]);

    public Task<Todo?> Find(int id) => Task.FromResult<Todo?>(null);

    public Task<bool> TitleExists(string title) => Task.FromResult(false);

    public Task<Todo> Add(string title) => Task.FromResult(new Todo(1, title, false));

    public Task<bool> Remove(int id) => Task.FromResult(false);
}
```

`[TestExport]` on `EmptyStoreTests` registers `EmptyTodoStore` for every test in the class:

```csharp
using DependencyModules.Testing.Attributes;
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Todos.Tests;

[TestExport(typeof(ITodoStore), Implementation = typeof(EmptyTodoStore))]
public class EmptyStoreTests
{
    [ModuleTest]
    public async Task ListsNothing(ITestWebApp app)
    {
        var todos = (await app.Get("/todos")).Deserialize<List<Todo>>();

        Assert.Empty(todos);
    }
}
```

The runner registers an export after the application's modules, so it replaces the application's
registration. A `[Mock]` of the same service on a test still replaces the export.

`Lifetime` sets the registration's lifetime. The default is `ServiceLifetime.Transient`. A test
parameter of the exported service is the object the requests use only when `Lifetime` is
`ServiceLifetime.Singleton`. With the default, the test's instance and each request's are
different objects.

On the pipeline host, a singleton export that no parameter holds is built again for each request.
`Shared = true` keeps one instance across the requests.

An attribute that implements `IHardenedTestDependencyRegistrationAttribute` does the same in code.
Its registrations also come after the application's modules.

```csharp
using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Tests;

public sealed class EmptyStoreAttribute : Attribute, IHardenedTestDependencyRegistrationAttribute
{
    public void RegisterDependencies(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceCollection serviceCollection
    )
    {
        serviceCollection.AddSingleton<ITodoStore, EmptyTodoStore>();
    }
}
```

With `[EmptyStore]` on a test, `GET /todos` lists nothing:

```csharp
    [ModuleTest]
    [EmptyStore]
    public async Task ListsNothing(ITestWebApp app)
    {
        var todos = (await app.Get("/todos")).Deserialize<List<Todo>>();

        Assert.Empty(todos);
    }
```

## Writing a test attribute

An attribute that implements one of five interfaces in `Hardened.Shared.Testing.Attributes` runs
at a fixed point while each test is built. The runner calls them in this order:

| Interface | Runs | Receives |
|---|---|---|
| `IHardenedTestEnvironmentAttribute` | When the environment is built, before the modules, after the `[EnvironmentValue]` values are in | The environment's name, and its values as an `IDictionary<string, object>` to change |
| `IHardenedTestDependencyRegistrationAttribute` | After the application's modules | The `IServiceCollection` |
| `IHardenedParameterProviderAttribute` | After the registration attributes | The `IServiceCollection`, and `null` for the parameter |
| `IHardenedTestConfigurationAttribute` | After the parameter providers | An `IAppConfig` |
| `IHardenedTestStartupAttribute` | After the container is built and the application's startup services have run, in every container the test builds | The `IServiceProvider` |

The runner reads these attributes from the method, its class and the assembly. All five
interfaces extend `IHardenedOrderedAttribute`. Its `Order` defaults to 10. Attributes of one
interface run in `Order`, lowest first, across the method, the class and the assembly.

Each interface method also receives an `AttributeCollection` and the test's `MethodInfo`. The
collection holds the attributes of the method, its class and the assembly. `GetAttribute<T>()`
returns the attribute of type `T` from the narrowest of the three. `GetAttributes<T>()` returns
all of them.

An environment attribute can set any number of values. Each attribute target can carry only one
`[EnvironmentValue]`. `FeatureFlagsAttribute` sets two values:

```csharp
using System.Reflection;
using Hardened.Shared.Testing.Attributes;

namespace Todos.Tests;

public sealed class FeatureFlagsAttribute : Attribute, IHardenedTestEnvironmentAttribute
{
    public void ConfigureEnvironment(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        string environmentName,
        IDictionary<string, object> environment
    )
    {
        environment["TODOS_EXPORT"] = "on";
        environment["TODOS_ARCHIVE"] = "on";
    }
}
```

An environment attribute's value replaces an `[EnvironmentValue]` for the same variable.
[Writing a test](/guide/testing) covers `[EnvironmentValue]`.

The runner never calls `IHardenedParameterProviderAttribute.ProvideParameterValue`. A parameter
comes from the container, so a registration attribute that registers the parameter's type
supplies it.

The `IAppConfig` that a configuration attribute receives is registered as the test's
`IConfigurationPackage`. Its amendments run when a model is first built, after the application's
amendments. [Configuration](/guide/configuration) covers `Amend`. `PageSizeAttribute` amends
`TodoListOptions`, the model from that page:

```csharp
using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Testing.Attributes;

namespace Todos.Tests;

public sealed class PageSizeAttribute(int size) : Attribute, IHardenedTestConfigurationAttribute
{
    public void Configure(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IAppConfig appConfig
    )
    {
        appConfig.Amend((TodoListOptions options) => options.PageSize = size);
    }
}
```

`ExtraTodoAttribute` is a startup attribute that adds a todo through the application's
`ITodoStore`:

```csharp
using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Tests;

public sealed class ExtraTodoAttribute(string title) : Attribute, IHardenedTestStartupAttribute
{
    public async Task Startup(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceProvider serviceProvider
    )
    {
        await serviceProvider.GetRequiredService<ITodoStore>().Add(title);
    }
}
```

::: warning
A startup attribute runs again in each container that a request runs in. A service that the test
takes as a parameter is the same object in all of those containers, so the attribute runs on it
once per container. With `ITodoStore` as a parameter and `[ExtraTodo("Write a test")]`, the store
holds the seeded todo twice after one request.
:::

The tests in `TestAttributeTests` carry the four attributes:

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Testing;
using Xunit;

namespace Todos.Tests;

public class TestAttributeTests
{
    [ModuleTest]
    [EmptyStore]
    public async Task ListsNothing(ITestWebApp app)
    {
        var todos = (await app.Get("/todos")).Deserialize<List<Todo>>();

        Assert.Empty(todos);
    }

    [ModuleTest]
    [ExtraTodo("Write a test")]
    public async Task EveryRequestSeesTheSeededTodo(ITestWebApp app)
    {
        var todos = (await app.Get("/todos")).Deserialize<List<Todo>>();

        Assert.Equal([1, 2, 3], todos.Select(todo => todo.Id));
    }

    [ModuleTest]
    [PageSize(1)]
    public async Task ListsOneTodo(ITestWebApp app)
    {
        var todos = (await app.Get("/todos")).Deserialize<List<Todo>>();

        Assert.Single(todos);
    }

    [ModuleTest]
    [FeatureFlags]
    public void TurnsOnBothFlags(IHardenedEnvironment environment)
    {
        Assert.Equal("on", environment.Value<string>("TODOS_EXPORT"));
        Assert.Equal("on", environment.Value<string>("TODOS_ARCHIVE"));
    }
}
```

`LocalDynamoDbAttribute` in `Hardened.Aws.DynamoDbClient.Testing` is a registration and startup
attribute to derive from. It points the application's `IDynamoDbClientProvider` at DynamoDB Local
in a container. A derived class overrides `DdbSetup` to create tables. The AWS
[DynamoDB client](/aws/dynamodb) page covers it.

## Next

| Page | Covers |
|---|---|
| [Writing a test](/guide/testing) | The parameters a test takes, and what each test builds |
| [Sending requests](/guide/testing-web) | Requests through `ITestWebApp` |
| [Configuration](/guide/configuration) | Configuration models and `Amend` |
| [DynamoDB client](/aws/dynamodb) | DynamoDB Local in a test |
