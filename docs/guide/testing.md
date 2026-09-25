# Writing a test

`[ModuleTest]` on a test method builds the application for that one test, then calls the method
with its parameters. A parameter can be any service the application registers, or something the
testing packages supply, such as `ITestWebApp`.

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Todos.Tests;

public class TodoStoreTests
{
    [ModuleTest]
    public async Task DeleteRemovesTheTodo(ITestWebApp app, ITodoStore store)
    {
        var response = await app.Delete("/todos/1");

        Assert.Equal(204, response.StatusCode);
        Assert.Null(await store.Find(1));
    }
}
```

`ITestWebApp` sends requests into the application. The DELETE handler and the test use the same
`ITodoStore`. [What every request shares](#what-every-request-shares) says why.

## Setting up a test project

`dotnet new hardened-web` writes a test project that is already set up, in `tests/Todos.Tests`.
`--test-framework` and `--mocks` choose its runner and its mock library. `Directory.Packages.props`
holds the package versions. [Project templates](/guide/project-templates) covers both options and
that file.

A test project references `Hardened.Shared.Testing`, one runner package, and the runner's own
packages. The runner packages are DependencyModules packages. The tests of a web application also
reference `Hardened.Web.Testing`.

| Package | Brings |
|---|---|
| `Hardened.Shared.Testing` | `[HardenedTestEntryPoint]`, `ITestContext`, `[EnvironmentName]`, `[EnvironmentValue]`, the test attribute interfaces, and the logger that writes the application's log to the test's output. It brings `DependencyModules.Testing`, which holds `[Mock]`, `[Shared]` and `CurrentTest` |
| `DependencyModules.xUnit` | `[ModuleTest]` for `xunit.v3` 3.x |
| `DependencyModules.xUnit4` | `[ModuleTest]` for `xunit.v3` 4.x |
| `DependencyModules.NUnit` | `[ModuleTest]` for NUnit 4 |
| `Hardened.Web.Testing` | `[WebTesting]` and `ITestWebApp` |
| `DependencyModules.NSubstitute`, `DependencyModules.Moq` or `DependencyModules.FakeItEasy` | The mock library behind `[Mock]` |

| Runner | Runner package | The runner's own packages in the template |
|---|---|---|
| xUnit v3, the template's default | `DependencyModules.xUnit` | `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` |
| NUnit 4 | `DependencyModules.NUnit` | `NUnit`, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk` |

Each runner package defines a `[ModuleTest]`. The xUnit attribute is in
`DependencyModules.xUnit.Attributes`, and the NUnit attribute is in
`DependencyModules.NUnit.Attributes`. The xUnit `[ModuleTest]` is an xUnit v3 `FactAttribute`. A
project that references `xunit` 2.x in place of `xunit.v3` fails to compile with `CS0433` on
`Assert`.

A test project that references `Hardened.Shared.Testing` and no runner package builds with the
warning `HRDT001`, then fails with `CS0246` on each `[ModuleTest]`. For a project named
`NoRunner.Tests`, the warning reads:

```text
NoRunner.Tests references Hardened.Shared.Testing and no DependencyModules test package, so [ModuleTest] is not defined and every test method carrying it is CS0246. Add a PackageReference to DependencyModules.xUnit for xunit.v3 3.x, DependencyModules.xUnit4 for xunit.v3 4.x, or DependencyModules.NUnit.
```

The template writes `tests/Todos.Tests/Todos.Tests.csproj`, shown here without its comments:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Hardened.Shared.Testing" />
    <PackageReference Include="DependencyModules.xUnit" />
    <PackageReference Include="DependencyModules.NSubstitute" />
    <PackageReference Include="Hardened.Web.Testing" />
    <PackageReference Include="Hardened.Web.Kestrel.Testing" />
    <PackageReference Include="Hardened.Kiota.Testing" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Todos\Todos.csproj" />
    <ProjectReference Include="..\..\src\Todos.Client\Todos.Client.csproj" />
  </ItemGroup>

</Project>
```

The test project references the library project `src/Todos` and the client project, not the host
project.

### Assembly attributes

`tests/Todos.Tests/Bootstrap.cs` holds assembly attributes that apply to every test in the project.
The template writes this file, shown here without its comments:

```csharp
using DependencyModules.NSubstitute;
using Hardened.Kiota.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Testing;
using Hardened.Web.Testing;
using Todos;

[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(TodosLibrary))]
[assembly: NSubstituteSupport]
[assembly: KestrelTesting]
[assembly: KiotaTesting]
```

| Attribute | Package | Does | Covered on |
|---|---|---|---|
| `[assembly: WebTesting]` | `Hardened.Web.Testing` | Makes `ITestWebApp`, `HttpClient` and generated clients test parameters | [Sending requests](/guide/testing-web) |
| `[assembly: HardenedTestEntryPoint(typeof(TodosLibrary))]` | `Hardened.Shared.Testing` | Names the module each test builds | [The entry point](#the-entry-point) |
| `[assembly: NSubstituteSupport]` | `DependencyModules.NSubstitute` | Names the mock library behind `[Mock]` | [Substituting services](/guide/testing-mocks) |
| `[assembly: KestrelTesting]` | `Hardened.Web.Kestrel.Testing` | Runs a test marked `[KestrelRuntime]` on a real socket | [Test hosts](/guide/testing-hosts) |
| `[assembly: KiotaTesting]` | `Hardened.Kiota.Testing` | Makes the generated Kiota client a test parameter | [Typed clients](/guide/testing-clients) |

### The entry point

`[HardenedTestEntryPoint]` names a module. Each test applies that module and every module it
imports. The template's tests build `TodosLibrary`. The host project's module, `Application`, is not
part of any test. Neither is anything it adds.

The attribute can also go on a class or a method. There it adds its module to the one the assembly
names. Both modules apply. With two entry points in scope, each one runs the test's registration and
startup attributes. Each such attribute then runs twice.

## Test parameters

A test method can take these parameters:

| Parameter | What the test receives | Covered on |
|---|---|---|
| A service the application registers | The registration, resolved from the test's container | [What each test builds](#what-each-test-builds) |
| A class nothing registers | A new instance, its constructor's parameters resolved from the container | [What each test builds](#what-each-test-builds) |
| `IServiceProvider` | The test's container | [What each test builds](#what-each-test-builds) |
| `ITestContext` | Steps, retries and a logger | [Steps and retries](#steps-and-retries) |
| `[Mock] T` | A test double, registered over the application's `T` | [Substituting services](/guide/testing-mocks) |
| `ITestWebApp` or `HttpClient` | A client that sends requests into the application | [Sending requests](/guide/testing-web) |
| A generated client | The Kiota or Refit client, sending into the application | [Typed clients](/guide/testing-clients) |
| A trigger façade | Delivers a trigger to a function handler | [Testing functions](/guide/testing-functions) |

A parameter the container cannot supply, such as an interface nothing registers, fails the test
before the method runs. Under `[WebTesting]`, the failure's message describes the ways it builds a
client for a parameter. For an interface `INothingRegistersThis` that nothing registers, the message
begins:

```text
Todos.Tests.INothingRegistersThis cannot be built for a test parameter. None of the three routes applies:
```

In xUnit, `[InlineData]` beside `[ModuleTest]` fills the leading parameters. The runner resolves
the rest. Each row is a test of its own, with a container of its own.

## What each test builds

Each test gets a container of its own. For each test, the runner takes these steps in order:

1. It registers the test's environment. [Setting the environment](#setting-the-environment) covers
   it.
2. It applies the entry point's module and every module it imports.
3. It runs the setup attributes in scope: the entry point, `[WebTesting]` and `[TestExport]`. The
   entry point makes its own registrations and runs the test's registration attributes. The setup
   attributes run after the modules, so a service they register replaces the application's
   registration of it.
4. It registers each `[Mock]` parameter, last.
5. It builds the container.
6. It runs the application's startup services, then the test's startup attributes. It also starts
   the host.
7. It resolves the parameters and calls the method.
8. When the test has run, it disposes every container the test built.

A startup service that throws fails the test with the service's exception. A startup service that
returns `false` does not stop the test. [Substituting services](/guide/testing-mocks) covers
registration attributes, startup attributes, `[TestExport]` and `[Mock]`.

The runner removes the application's logger providers and adds one that writes to the test's
output. [The test output](#the-test-output) shows a line of it.

## A container for each request

A test runs on the pipeline host unless it names another host. On that host, every request that the
test sends through `ITestWebApp`, an `HttpClient` or a generated client runs in a new
container. Each of those containers is built from the test's registrations. The application's
startup services run again in each one.

A singleton that a handler writes to is therefore new in each request. The template's store starts
with two todos, ids 1 and 2. In `ATodoOneRequestCreatesIsGoneByTheNext`, the todo that the POST
created is gone by the GET:

```csharp
using DependencyModules.Testing.Attributes;
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Todos.Tests;

public class RequestContainerTests
{
    [ModuleTest]
    public async Task ATodoOneRequestCreatesIsGoneByTheNext(ITestWebApp app)
    {
        await app.Post(new NewTodo("Write a test"), "/todos");

        var todos = (await app.Get("/todos")).Deserialize<List<Todo>>();

        Assert.Equal([1, 2], todos.Select(todo => todo.Id));
    }

    [ModuleTest]
    public async Task SharedSendsEveryRequestToOneContainer([Shared] ITestWebApp app)
    {
        await app.Post(new NewTodo("Write a test"), "/todos");

        var todos = (await app.Get("/todos")).Deserialize<List<Todo>>();

        Assert.Equal([1, 2, 3], todos.Select(todo => todo.Id));
    }
}
```

Not every host builds a container per request.

| Host | `ContainerPolicy` | A container per request |
|---|---|---|
| The pipeline: the default, and a test marked `[PipelineHost]` | `PerInvocation` | Yes |
| `[LambdaWebTesting]` | `PerInvocation` | Yes |
| `[KestrelRuntime]` under `[assembly: KestrelTesting]`, and `[AspNetCoreRuntime]` under `[assembly: AspNetCoreTesting]` | `Reused` | No |
| `[AzureFunctionsWebTesting]` | `Reused` | No |

A test can take `ITestHost` as a parameter and read its `ContainerPolicy`.
[Test hosts](/guide/testing-hosts) covers choosing a host.

## What every request shares

Every test parameter is one object for the whole test. Each container the test builds is given the
test's instance. The parameters that send requests are the exception: `ITestWebApp`, `HttpClient`,
a generated client and a trigger façade.

In `DeleteRemovesTheTodo`, at the top of this page, `store` is a parameter. The container that the
DELETE ran in was therefore given the test's `ITodoStore`.

The requests use the test's instance only when the service is a singleton. For a transient service,
the test and each request get different objects.

The runner also keeps five of its own services across the containers: `IHardenedEnvironment`,
`IModuleEnvironment`, `IConfigurationPackage`, `ITestContext` and `TestCancellationToken`. A
`[Mock]` is kept across the containers too. Every other registration is built again in each
container.

## Sending every request to one container

`[Shared]` on an `ITestWebApp` or `HttpClient` parameter sends every request it makes to the test's
own container. That is the container the parameters were resolved from.
`SharedSendsEveryRequestToOneContainer`, in the example under
[A container for each request](#a-container-for-each-request), uses it.

The attribute is `SharedAttribute`, in the namespace `DependencyModules.Testing.Attributes`. It goes
on a parameter only. It is ignored on some parameters that send requests. On those, each request
still runs in a container of its own.

| Parameter carrying `[Shared]` | One container |
|---|---|
| `ITestWebApp` or `HttpClient` | Yes |
| A Refit client | Yes |
| A client whose constructor takes one `HttpClient` | Yes |
| A client that an `ITestClientFactory<T>` builds from the `HttpClient` it is given | Yes |
| A Kiota client | No |
| A client that an `ITestClientFactory<T>` builds in `Create(TestClientContext)` over `CreateHttpClient` | No |
| Any of these with `[Grants]`, `[Subject]` or `[Anonymous]` on the same parameter | No. With the credential attribute on the method, yes |

[Typed clients](/guide/testing-clients) covers client factories.
[Sending requests](/guide/testing-web) covers the credential attributes.

## Setting the environment

A test's environment is named `test`. `[EnvironmentName("production")]` names it instead.
`[EnvironmentValue("TODOS_PAGE_SIZE", "5")]` gives it a variable, which
`IHardenedEnvironment.Value<T>` reads. The attribute does not set a variable in the process.
[Environments](/guide/environments) covers the rest of a test's environment.

The runner registers the environment before it applies the modules. A registration that depends on
the environment therefore follows the test's environment name. With the `IEmailSender` classes from
[Registering services](/guide/services), a test in `production` gets `SmtpEmailSender`. A test in
`test` gets `ConsoleEmailSender`.

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Todos.Tests;

public class EnvironmentTests
{
    [ModuleTest]
    [EnvironmentName("production")]
    [EnvironmentValue("TODOS_PAGE_SIZE", "5")]
    public void ReadsTheDeclaredEnvironment(IHardenedEnvironment environment)
    {
        Assert.Equal("production", environment.Name);
        Assert.Equal(5, environment.Value<int>("TODOS_PAGE_SIZE"));
    }

    [ModuleTest]
    [EnvironmentName("production")]
    public void UsesTheProductionSender(IEmailSender sender)
    {
        Assert.IsType<SmtpEmailSender>(sender);
    }
}
```

Both attributes are in `Hardened.Shared.Testing.Attributes`. Each goes on a method, a class or the
assembly. For `[EnvironmentName]`, the narrowest wins: a method's over its class's, and a class's
over the assembly's. The values from `[EnvironmentValue]` on the method, the class and the assembly
are merged. When two of them set the same variable, the widest wins: the assembly's over the
class's, and the class's over the method's.

A method, a class or the assembly takes one `[EnvironmentValue]`. A second on the same method fails
to compile with `CS0579`, "Duplicate 'EnvironmentValue' attribute". A custom environment attribute
sets any number of values. [Substituting services](/guide/testing-mocks) covers the interface it
implements.

## Steps and retries

`ITestContext` logs named steps and retries a check until it holds. It is in the namespace
`Hardened.Shared.Testing`. `ITestWebApp` extends it. A test that takes `ITestWebApp` calls the steps
and retries on that parameter:

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Todos.Tests;

public class StepTests
{
    [ModuleTest]
    public async Task CreatesATodo(ITestWebApp app, ITodoStore store)
    {
        await app.Step(() => app.Post(new NewTodo("Write a test"), "/todos"), "Create a todo");

        await app.Retry.TillTrue(async () => await store.Find(3) is not null, "Todo {Id} is stored", 3);

        var todo = await app.Step(() => store.Find(3), "Read todo {Id}", 3);

        Assert.Equal("Write a test", todo!.Title);
    }
}
```

`ITestContext` has four members:

| Member | Does |
|---|---|
| `Step(step, description, parameters)` | Runs `step`, logs whether it passed and how long it took, and returns what `step` returned. The four overloads take an `Action`, a `Func<T>`, a `Func<Task>` and a `Func<Task<T>>` |
| `Retry` | The retry methods in the next table |
| `Logger` | An `ILogger` that writes to the test's output. Its category is the test class's full name |
| `CancellationRequest` | A `CancellationToken` that is never cancelled in a `[ModuleTest]` |

A step that passes logs `pass - <description> - <milliseconds>ms` at Information. When the delegate
throws, the step logs `fail` at Error. The exception then propagates to the test.

The description is a logging message template. The `parameters` fill its placeholders. The
description `"Read todo {Id}"` with `3` logged `pass - Read todo 3 - 0.02725ms`. Each parameter
needs a placeholder. A parameter without one is logged where the duration goes. The description
`"No placeholder here"` with `42` logged `pass - No placeholder here - 42ms`.

`Retry` has three methods:

| Method | Calls the function until |
|---|---|
| `TillTrue(Func<Task<bool>>, description, parameters)` | It returns `true` |
| `TillFalse(Func<Task<bool>>, description, parameters)` | It returns `false` |
| `TillValue<T>(Func<Task<T>>, description, parameters)` | It returns without throwing. `TillValue` then returns that value |

Each attempt logs the description at Information. An attempt that throws is logged. The method then
tries again. Attempts are one second apart. `TillValue` returns `null` at once when the function
returns `null`. It waits only while the function throws.

In an xUnit v3 project, `using Xunit;` also brings `Xunit.ITestContext`. With both namespaces
imported, an unqualified `ITestContext` fails to compile with `CS0104`, "'ITestContext' is an
ambiguous reference between 'Hardened.Shared.Testing.ITestContext' and 'Xunit.ITestContext'". The
template's `Usings.cs` has `global using Xunit;`. A `using` alias resolves the name:

```csharp
using DependencyModules.xUnit.Attributes;
using Xunit;
using ITestContext = Hardened.Shared.Testing.ITestContext;

namespace Todos.Tests;

public class StoreStepTests
{
    [ModuleTest]
    public async Task AddsATodo(ITestContext context, ITodoStore store)
    {
        var todo = await context.Step(() => store.Add("Write a test"), "Add a todo");

        Assert.Equal(3, todo.Id);
    }
}
```

An NUnit project has no such conflict.

### The test output

Each log line goes to the test's output as an indented JSON record. The test's output is xUnit's
test output, or NUnit's `TestContext.Out`. The application's own log lines appear there too.

`dotnet test` prints a test's output when the test fails. This command prints it for passing tests
too:

```bash
dotnet test --logger "console;verbosity=detailed"
```

The first step of `CreatesATodo` logs this record. The console prints each line with one more
leading space.

```json
{
  "timestamp": "2026-09-23T20:30:37.440051-04:00",
  "logger": "Todos.Tests.StepTests",
  "logLevel": "Information",
  "eventId": {
    "id": 0,
    "name": null
  },
  "message": "pass - Create a todo - 55.959500000000006ms",
  "data": [
    {
      "key": "status",
      "value": "pass"
    },
    {
      "key": "duration",
      "value": 55.959500000000006
    },
    {
      "key": "{OriginalFormat}",
      "value": "{status} - Create a todo - {duration}ms"
    }
  ],
  "exception": null
}
```

## Limits

`Retry.Delay` is not read. Setting it does not change the interval between attempts.

The retry methods have no limit on attempts. `CancellationRequest` is never cancelled in a
`[ModuleTest]`. A condition that never holds keeps the test running until the runner stops it. Under xUnit, `[ModuleTest(Timeout = 3000)]` fails such a test after 3 seconds with `Test execution timed out after 3000 milliseconds`.

## Next

| Page | Covers |
|---|---|
| [Sending requests](/guide/testing-web) | Sending requests with `ITestWebApp`, and checking the response |
| [Typed clients](/guide/testing-clients) | The generated client as a test parameter |
| [Substituting services](/guide/testing-mocks) | `[Mock]`, fake classes and the test attribute interfaces |
| [Test hosts](/guide/testing-hosts) | Running a test on Kestrel, ASP.NET Core or a cloud host |
| [Testing functions](/guide/testing-functions) | Testing function handlers and their triggers |
