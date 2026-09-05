# Writing a test

A Hardened test boots the real application and hands the test method whatever it asks for. A
service, a mock, a request client and a generated API client are all parameters.

```csharp
public class TodoTests {

    [HardenedTest]
    public async Task CreateTodo_AnswersCreated(TodosClient client, [Mock] ITodoStore store) {
        store.Add("ship it").Returns(new Todo(7, "ship it", false));

        var created = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
            .Returns<Created<ClientModels.Todo>>();

        Assert.Equal("/todos/7", created.Location);
    }
}
```

Three things happened before the method body ran.

- `client` is the Kiota client generated from the application's own OpenAPI document. It sends
  through an `HttpClient` whose handler runs the application's pipeline in-process. See
  [Typed clients](/guide/testing-clients).
- `store` is an NSubstitute mock, registered in the container the handler resolves from. The
  handler behind `POST /todos` used it. See [Substituting services](/guide/testing-mocks).
- `Returns<Created<ClientModels.Todo>>()` checked that the call answered 201 and handed back the
  body and the `Location` header. See [Asserting a response](/guide/testing-responses).

Nothing in the test names a port, a host or a serializer. `dotnet new hardened-web` writes a test
project in this shape, and `ClientModels` is the alias that project declares for the client's
model namespace.

## What a test boots

`[HardenedTest]` builds the application the way a host would. The module graph is applied,
configuration is resolved, startup services run, and each parameter of the test method is
resolved from the provider. Any service the application registers is a parameter the test can
ask for:

```csharp
public class MathServiceTests {

    [HardenedTest]
    public void AddsValues(IMathService<int> mathService) {
        Assert.Equal(6, mathService.Add(1, 2, 3));
    }
}
```

The application is built for each test and disposed when the test ends.

## Setting up a project

The test project references the testing package and a runner package:

```xml
<ItemGroup>
    <PackageReference Include="Hardened.Shared.Testing" />
    <PackageReference Include="Hardened.Shared.Testing.xUnit" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
</ItemGroup>
```

`[HardenedTest]` comes from the runner package:

| Runner | Package | `[HardenedTest]` is |
|---|---|---|
| xUnit v3 | `Hardened.Shared.Testing.xUnit` | a `FactAttribute`. `dotnet test`, the IDE runner and every xUnit assertion work unchanged |
| NUnit | `Hardened.Shared.Testing.NUnit` | NUnit's test attribute, so the NUnit adapter discovers it |

The xUnit package builds on xUnit v3. A project on xunit 2.x fails to compile with `CS0433` on
`Assert`.

An assembly attribute names the module under test:

```csharp
// Bootstrap.cs
using Hardened.Shared.Testing.Attributes;

[assembly: HardenedTestEntryPoint(typeof(TodosLibrary))]
```

On the assembly it covers every test. On a class or a method it names a different module for
those tests.

A web application adds `[assembly: WebTesting]` from `Hardened.Web.Testing`; see
[Sending requests](/guide/testing-web#setup).

## Parameters

| Parameter | What arrives |
|---|---|
| Any registered service | The application's own registration, resolved from the test's container |
| `[Mock] T` | An NSubstitute substitute for `T`, registered over the application's registration. [Substituting services](/guide/testing-mocks) |
| `ITestContext` | Named steps, a retry engine, a logger and the test's cancellation token. [Steps and retries](/guide/testing-steps) |
| `ITestWebApp` | Sends requests through the pipeline. [Sending requests](/guide/testing-web) |
| A client type | A Kiota client, a Refit interface or any class taking one `HttpClient`, built over the pipeline. [Typed clients](/guide/testing-clients) |
| `LambdaTestApp`, `TestSqsApp`, `TestDynamoDbStream` | The Lambda harnesses. [Testing AWS handlers](/aws/testing) |

A parameter nothing can supply fails the test.

## Environments in tests

The environment is named `test` unless the test says otherwise:

```csharp
[HardenedTest]
[EnvironmentName("production")]
[EnvironmentValue("FEATURE_X", "on")]
public void UsesTheProductionSender(IEmailSender sender) {
    Assert.IsType<SmtpEmailSender>(sender);
}
```

`[EnvironmentName]` changes the name that `[IfEnvironment]` and environment-scoped configuration
amenders are evaluated against. `[EnvironmentValue]` sets a variable for the test alone, without
touching the process. Both are valid on a method, a class or the assembly, and the narrowest
wins.

The environment is registered before the modules are applied, so a registration gated on the
name is decided against the test's name rather than a process default.

## Where the tests point

The template's test project references the library and not the host. `ITestWebApp` and the client
parameters drive the pipeline the library declares, so the same tests hold whether the host is
Kestrel, ASP.NET Core or Lambda. A test that needs the host names one; see
[Test hosts](/guide/testing-hosts).

## Next

- [Sending requests](/guide/testing-web): `ITestWebApp`, the request methods and the response
- [Substituting services](/guide/testing-mocks): `[Mock]`, fakes and environment-gated registrations
- [Credentials](/guide/testing-credentials): who a request is sent as
- [Typed clients](/guide/testing-clients): a generated client as a parameter
- [Asserting a response](/guide/testing-responses): `Returns<T>()`, `ReturnsStatus<T>()` and `LastResponse`
- [Test hosts](/guide/testing-hosts): the same tests on Kestrel or ASP.NET Core
- [Steps and retries](/guide/testing-steps): `ITestContext`
- [Writing a test attribute](/guide/testing-attributes): setup shared by many tests
- [Testing AWS handlers](/aws/testing): Lambda functions, SQS batches, stream records, DynamoDB Local
