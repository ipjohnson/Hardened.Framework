# Clients

Hardened generates the document; Kiota generates the client. The framework writes the OpenAPI
document a service serves to a file during the build, for every contract style and without running
the application, and the `hardened-web` template scaffolds a C# client from that file with a test
that drives it through the in-process pipeline. There is no Hardened package around any generator:
the file is the deliverable, and the same file feeds every other generator and language.

## The export

One MSBuild property, on the project whose build produces the document:

```xml
<PropertyGroup>
  <HardenedOpenApiOutput>openapi/Todos.json</HardenedOpenApiOutput>
</PropertyGroup>
```

The path is relative to the project. After every compile the build writes the document the
application serves at `/openapi.json` — the normalised document generated from the model, never
the source contract — read out of the compiled assembly rather than out of a running application,
so it works for a Lambda function and for a library with no entry point. The extension decides the
format: `.json` writes indented JSON, `.yaml` or `.yml` writes YAML, and anything else is a build
error naming the three. What the application serves does not change whatever the file says.

Code-first, the module that declares the routes needs `[Enable<OpenApiDocumentPublishing>]`, and
the build says so (`HRDOA018`) if the property is set without it. Contract-first, every project
that generated from a contract already carries the document.

`HardenedOpenApiOutputVersion` lowers the written file to `3.0.0` or `3.1.0` for a reader that
refuses the 3.2 banner Hardened emits by default — NSwag's is 3.0-first, and Spectral's `oas`
ruleset stops at 3.1. The served document is untouched. A streaming operation loses its
`itemSchema` on the way down, which arrived in 3.2, and the build names each one (`030`). See
[the OpenAPI document](/guide/openapi-document) for the whole vocabulary.

## The client project

The template writes `src/Todos.Client`, a project with no hand-written code. This is the whole of
it, as the template writes it:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Todos.Client</RootNamespace>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <IsPackable>true</IsPackable>
    <PackageId>Todos.Client</PackageId>
    <KiotaSpec>../Todos/openapi/Todos.json</KiotaSpec>
    <KiotaOutput>$(BaseIntermediateOutputPath)kiota/</KiotaOutput>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Kiota.Bundle" />
    <ProjectReference Include="../Todos/Todos.csproj"
                      ReferenceOutputAssembly="false"
                      SkipGetTargetFrameworkProperties="true" />
  </ItemGroup>

  <Target Name="RestoreKiota" Condition="'$(DesignTimeBuild)' != 'true'">
    <Exec Command="dotnet tool restore" StandardOutputImportance="low" />
  </Target>

  <Target Name="GenerateClient" DependsOnTargets="RestoreKiota" BeforeTargets="CoreCompile"
          Condition="'$(DesignTimeBuild)' != 'true'"
          Inputs="$(KiotaSpec)" Outputs="$(KiotaOutput)kiota-lock.json">
    <Exec Command="dotnet kiota generate --language CSharp --openapi &quot;$(KiotaSpec)&quot; --class-name TodosClient --namespace-name $(RootNamespace) --output &quot;$(KiotaOutput)&quot; --clean-output --exclude-backward-compatible --log-level warning" />
  </Target>

  <Target Name="IncludeClient" AfterTargets="GenerateClient" BeforeTargets="CoreCompile">
    <ItemGroup>
      <Compile Include="$(KiotaOutput)**/*.cs" />
    </ItemGroup>
  </Target>

  <Target Name="CleanClient" AfterTargets="CoreClean">
    <RemoveDir Directories="$(KiotaOutput)" />
  </Target>

</Project>
```

The scaffolded file carries a comment on each choice; the short version is this. The tool is
restored inside the build, which is what lets a fresh clone build with no instruction beyond
`dotnet build`. Output goes under `obj/` and is added to the compilation inside a target, because
the SDK's `**/*.cs` glob is evaluated before any target runs. `kiota-lock.json` is the up-to-date
stamp, so an unchanged document skips Kiota. The project reference orders the build and nothing
else: `ReferenceOutputAssembly="false"` keeps the server, and the host with it, out of the client,
whose only dependency is `Microsoft.Kiota.Bundle`. And the client is `net8.0` whatever the library
is, so a library on `net11.0` for the union response model needs no negotiation with it.

The scaffold also checks its two pins agree — the tool in `.config/dotnet-tools.json` and
`KiotaBundleVersion` in `Directory.Packages.props` — and fails as `HTPL003` naming both files when
they do not, because the generated code and the runtime packages come from matching Kiota
releases. `HTPL002` is the tool failing to restore, which on a fresh machine means no network.

Nothing generated is committed; the document is. In CI, build and then check the file is current:

```bash
dotnet build
git diff --exit-code src/Todos/openapi
```

## The test

The generated client has one constructor, and it takes an `IRequestAdapter`. The framework does not
know that type and should not, so the test project says how to build the client from an
`HttpClient`, once:

```csharp
// tests/Todos.Tests/TestClients.cs
public sealed class TodosClientFactory : ITestClientFactory<TodosClient> {

    // The base URL is required by Kiota and ignored by the handler; a code-first document with no
    // [Server] leaves it unset otherwise. Credentials are already on the HttpClient.
    public TodosClient Create(HttpClient http) =>
        new(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http) {
            BaseUrl = "http://harness"
        });
}
```

With that in the test assembly, a test declares the client as a parameter and the harness builds
it over the in-process pipeline: no socket, no port, the same chain `ITestWebApp` drives.

```csharp
// tests/Todos.Tests/TodosClientTests.cs
public class TodosClientTests {

    [HardenedTest]
    public async Task GetTodo_ThroughTheGeneratedClient(TodosClient client) {
        var todo = await client.Todos[1].GetAsync();

        Assert.Equal("Read the generated code", todo!.Title);
    }

    /// <summary>
    /// What the client does not surface, the transport does: the status it did not throw on and the
    /// headers that came with it.
    /// </summary>
    [HardenedTest]
    public async Task CreateTodo_AnswersCreated(TodosClient client) {
        var todo = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" });

        Assert.Equal("ship it", todo!.Title);
        Assert.Equal(201, LastResponse.Status);
        Assert.Equal($"/todos/{todo.Id}", LastResponse.Headers["Location"]);
    }

    /// <summary>
    /// The 404 is in the signature, so it is in the document, so Kiota generated a typed exception
    /// for it - named after the case, NotFound, and carrying the body the server answered.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsATypedNotFound(TodosClient client) {
        var missing = await Assert.ThrowsAsync<ClientModels.NotFound>(() => client.Todos[9999].GetAsync());

        Assert.Equal(404, missing.ResponseStatusCode);
        Assert.Contains("9999", missing.Detail);
    }
}
```

`ClientModels` is an alias for `Todos.Client.Models` in the scaffold's `Usings.cs`: the generated
models are named after the schemas, and the schemas are named after the application's own types,
so a bare `NewTodo` in a test is the application's record.

Refusals are asserted with `Assert.ThrowsAsync` in the client's own vocabulary, which is typed for
every error body the document declares. `LastResponse` is the transport's record of what the
pipeline answered last in the current test, for everything a client library swallows: a 201 and
its `Location`, a 204, an `ETag`. Credentials are attributes — `[Grants("todos:write")]` on a
parameter, a method, a class or the assembly — and travel as headers on the `HttpClient`, so a
Kiota client, an NSwag client, a Refit interface and a hand-written class all authenticate the same
way with no code of their own. [Testing web handlers](/guide/testing-web) has the rest of the
harness.

The framework's own integration suite has the same shape over a much wider surface.
[`GeneratedClientTests`](https://github.com/ipjohnson/Hardened.Framework/blob/main/src/IntegrationTests/Web/Hardened.IntegrationTests.WebApp.SUT.Tests/Transport/GeneratedClientTests.cs) drives a client Kiota generated from that application's exported
document, and is the reference when the scaffold's three tests are not enough: path and query
parameters, a body in and a value out, one path under four verbs, a declared 201 and 204 read from
`LastResponse`, an enum arriving as the generated member, the typed `RequestValidationError` for a
declared 422 and for the default 400, a bare `ApiException` carrying 401 for a refusal the document
does not declare, three credentials on three parameters of the client type, and a `[Mock]` behind
a route reached through the client. The factory beside it, in `TestClients.cs`, is the one above
with the names changed.

Under `--response-model throws` the scaffold's test is different, and the difference is the
response-model argument in a consumer's hands: throws mode documents only the 200, so the generated
client has no 404 branch, and the same request that throws a typed `NotFound` under the response
model is a bare `ApiException` there. The scaffolded test says so in its comment.

## What Kiota does with a Hardened document

Kiota reads the 3.2 banner Hardened emits by default, and its .NET libraries strip a vendor prefix
when picking a parser, so the error mapping keeps working if the error envelope moves to
`application/problem+json`. For the template's four operations under the default response model:

| The document declares | Kiota generates | The consumer sees |
|---|---|---|
| 200/400/404 on `GET /todos/{id}`, 201/409 on `POST /todos`, 204/400/404 on `DELETE /todos/{id}`, with `NotFound`, `Conflict` and `RequestValidationError` bodies | Request builders per path, models, and an exception type per error schema mapped to its statuses | `await client.Todos[1].GetAsync()`; a declared 404 throws `NotFound`, a 400 throws `RequestValidationError`, both carrying the body |

Three things no scaffold gets from Kiota, stated here so they are never a defect report:

- A client signature that mirrors `Response<Todo, NotFound>`. Kiota's model is exceptions.
- A typed method for a streaming operation. Kiota ignores `text/event-stream` and does not read
  `itemSchema`, so an application that adds one gets a client without it.
- A composed type for two success cases at one status without a discriminator, which `HOAT022`
  already warns about on the server side.

A code-first document without `[Server]` has no `servers` entry, so Kiota says so on every
generation and the consumer sets `BaseUrl` on the adapter — the one line the scaffold's factory
carries. The template keeps that line out of the build's warning count.

## Other generators

Generator-specific code is confined to two places by construction: the client project's build
target and package references, and the factory in the test project where the constructor shape
needs one. Everything in a Hardened package — the export, the handler, the credentials, the
injection, the `HttpClient` convention — is the same for any of them. A client whose constructor
takes exactly one `HttpClient` is built with no factory at all, which is what NSwag's output and
most hand-written clients look like.

**NSwag.** NSwag's reader is 3.0-first, so set `HardenedOpenApiOutputVersion` to `3.0.0` on the
library and point an `nswag.json` at the file:

```json
{
  "documentGenerator": { "fromDocument": { "url": "../Todos/openapi/Todos.json" } },
  "codeGenerators": {
    "openApiToCSharpClient": {
      "className": "TodosClient",
      "namespace": "Todos.Client",
      "output": "obj/nswag/TodosClient.cs",
      "generateExceptionClasses": true
    }
  }
}
```

The client project's generate target becomes `dotnet tool run nswag run nswag.json`, the package
reference becomes `NSwag.MSBuild` or the tool manifest entry, and the test project needs no
factory: the generated class takes an `HttpClient`.

**Refitter.** `dotnet tool run refitter ../Todos/openapi/Todos.json --namespace Todos.Client
--output obj/refitter/TodosClient.cs` writes a Refit interface. Refit builds the implementation
from an `HttpClient` at run time, so the factory is `RestService.For<ITodosClient>(http)`.

**openapi-generator.** `openapi-generator-cli generate -g csharp -i src/Todos/openapi/Todos.json
-o clients/csharp`. It needs a Java runtime, which the verify matrix cannot assume, and its
generated project is a solution of its own rather than one target; treat it as a separate build.

**Other languages.** The same file. `kiota generate --language TypeScript --openapi
src/Todos/openapi/Todos.json --output clients/typescript` and the same for Java, Go, Python, PHP
and Ruby, each into a package manifest and toolchain of that language's own. Only C# is
scaffolded, because the verify matrix can run only what the .NET SDK provides.

## More than one language: the workspace

Kiota's workspace turns one document into several clients from one configuration, and it is the
documented migration for a second language rather than something the template writes. Three facts
first, stated plainly. The workspace commands have been in preview since Kiota 1.12 and were renamed
once, so they sit behind a flag. Their model assumes committed output: `apimanifest.json` records a
hash per client, so a team adopting it usually also commits the generated clients with the same
`git diff --exit-code` that guards the document. And every output path has to live inside the
workspace root, a rule added in 1.32.5.

```bash
export KIOTA_CONFIG_PREVIEW=true

kiota workspace init
kiota client add --client-name TodosClient --language CSharp \
  --openapi src/Todos/openapi/Todos.json --namespace-name Todos.Client \
  --output src/Todos.Client/obj/kiota --exclude-backward-compatible
kiota client add --client-name todos --language TypeScript \
  --openapi src/Todos/openapi/Todos.json --output clients/typescript/src/todos
```

The client project's target then becomes `dotnet kiota client generate --client-name TodosClient`,
and each further language is one more `kiota client add`. The document stays where the build
writes it, and the workspace reads it.
