# Generated clients

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

## Testing it

The scaffold's test project drives the client through the in-process pipeline. The client is a
test parameter, `[assembly: KiotaTesting]` builds it, and `Returns<T>()` asserts a call by naming
the response type the contract declares:

```csharp
[HardenedTest]
public async Task CreateTodo_AnswersCreatedWithALocation(TodosClient client) {
    var created = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
        .Returns<Created<ClientModels.Todo>>();

    Assert.Equal("ship it", created.Value.Title);
    Assert.Equal($"/todos/{created.Value.Id}", created.Location);
}
```

[Typed clients](/guide/testing-clients) covers how the client is built for a test, and
[Asserting a response](/guide/testing-responses) covers `Returns<T>()`, `ReturnsStatus<T>()` and
`LastResponse`. The framework's own
[`GeneratedClientTests`](https://github.com/ipjohnson/Hardened.Framework/blob/main/src/IntegrationTests/Web/Hardened.IntegrationTests.WebApp.SUT.Tests/Transport/GeneratedClientTests.cs)
drives a client over a much wider surface, and is the reference when the scaffold's tests are not
enough.

Under `--response-model throws` the document describes only the 200, so the generated client has
no 404 branch. The same request that answers a typed `NotFound` under the response model is a bare
`ApiException` there, asserted as `ReturnsStatus<NotFound>()`.

## Against a real service

Everything above builds the client for a test, over the in-process pipeline. The same project is
what other teams consume, and two things change: the base URL is real, and so is the credential.

### Ship it

The scaffolded client project is packable and its only dependency is the Kiota bundle, which is what
makes it worth shipping rather than asking each consumer to run a generator:

```bash
dotnet pack src/Todos.Client -p:PackageVersion=1.2.0
```

Version it with the service, not with the solution. The package's public surface is a function of
the document, so a version that moves when the contract moves is the one a consumer can reason
about. Nothing in the template pushes it anywhere.

The alternative is to publish the document and let each consumer generate. Both work off the same
file, and a consumer in another language has to do that anyway.

### Construct it

```csharp
var http = KiotaClientFactory.Create();

var client = new TodosClient(
    new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http) {
        BaseUrl = "https://todos.example.com"
    });
```

`KiotaClientFactory.Create()` returns an `HttpClient` carrying Kiota's own middleware: retry,
redirect, parameter-name decoding and the rest. **The test factory deliberately does not use it**,
because a test asserting a 429 or a 308 wants what the pipeline answered rather than what the
middleware made of it. In production it is what you want.

`BaseUrl` is required by Kiota. A code-first document with no [`[Server]`](/guide/openapi-document)
has no `servers` entry, so Kiota warns on every generation and this is the line that settles it.

### Authenticate it

The test harness sends credentials as headers through
[attributes](/guide/testing-credentials). A real consumer hands the adapter an authentication
provider instead:

```csharp
public sealed class TokenProvider : IAccessTokenProvider {

    public AllowedHostsValidator AllowedHostsValidator { get; } =
        new(["todos.example.com"]);

    public async Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default) =>
        await _tokens.AcquireFor(uri, cancellationToken);
}
```

```csharp
var adapter = new HttpClientRequestAdapter(
    new BaseBearerTokenAuthenticationProvider(new TokenProvider()), httpClient: http) {
        BaseUrl = "https://todos.example.com"
    };
```

`AllowedHostsValidator` is the part worth not skipping: it stops the token going out on a redirect
to somewhere else. `ApiKeyAuthenticationProvider` is the equivalent for a key in a header or query
value. These are Kiota's types, not Hardened's, and
[Kiota's authentication reference](https://learn.microsoft.com/openapi/kiota/authentication) is
where the rest of them are.

### Register it

```csharp
services.AddHttpClient("todos")
        .AddStandardResilienceHandler();

services.AddSingleton<IAuthenticationProvider>(
    new BaseBearerTokenAuthenticationProvider(new TokenProvider()));

services.AddSingleton(provider => new TodosClient(
    new HttpClientRequestAdapter(
        provider.GetRequiredService<IAuthenticationProvider>(),
        httpClient: provider.GetRequiredService<IHttpClientFactory>().CreateClient("todos")) {
        BaseUrl = "https://todos.example.com"
    }));
```

A **named** client rather than `AddHttpClient<TodosClient>()`, because the typed-client overload
constructs `TodosClient` from an `HttpClient` and a Kiota client's constructor takes an
`IRequestAdapter`. The same reason the test harness needs a factory for it.

`IHttpClientFactory` is what gives the client socket reuse and DNS refresh; a long-lived
`HttpClient` built by hand gets neither. `AddStandardResilienceHandler` comes from
`Microsoft.Extensions.Http.Resilience`. Use it or Kiota's own middleware, not both, or a failed
request is retried by each in turn.

::: tip The same client, both ways
Nothing about the client knows whether its `HttpClient` reaches a socket or the in-process
pipeline. That is the whole point of the transport: the client a consumer ships with is the client
the service's own tests drive.
:::

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

### Where the service and the document disagree

The three above are Kiota's shape. These are the service's, found by generating a client against it
and open at 0.19. A client makes them visible because it holds the document to its word.

::: warning Smithy in throws mode: a declared error arrives with no body
A Smithy `@error` shape is a named structure with a required `message`, and the document says the
404 carries it. In throws mode the handler answers that 404 by returning `null`, and the runtime
writes no body at all — `404` with `Content-Length: 0`. Kiota registered the shape for that status,
so the client throws a bare `ApiException` saying the error failed to deserialize, rather than the
typed `TodoNotFound`.

**The scaffold ships this test skipped** under `--contract smithy --response-model throws`, naming
the defect. Under `--response-model response` or `union` the handler returns the error case, the
body is written, and the same test passes.
:::

An OpenAPI service in throws mode has the milder version of it: a `null` return answers the
document's `Problem` with no `detail`. That deserializes, so the client throws the typed exception
either way and only the message is empty. Whether a null return should carry a default detail is
open.

**A Smithy model on the AWS JSON protocol produces a document no client can be generated from.**
Every operation is `POST /`, told apart by a header, so the document repeats the `post` key under
one path. That is the protocol's shape and the export carries it faithfully, but it is not
something an OpenAPI reader accepts — Microsoft.OpenApi's refuses it, and Kiota cannot generate
from it. A Smithy service that wants a generated client needs `@http` bindings that give each
operation its own method and path.

## Other generators

Generator-specific code is confined to two places by construction: the client project's build
target and package references, and the generator's testing package, which is where the constructor
shape and the shape of an answer are known. Everything in `Hardened.Web.Testing` — the export, the
handler, the credentials, the injection, the `HttpClient` convention — is the same for any of them.
A client whose constructor takes exactly one `HttpClient` is built with no package and no factory
at all, which is what NSwag's output and most hand-written clients look like; a generator with no
testing package gets a three-line `ITestClientFactory<T>` in the test project instead.

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

**Refitter.** `--client refit` scaffolds it: the client project restores the Refitter tool and
runs it over the exported document under the settings in its `.refitter` file, writing a Refit
interface and its models into `obj/`, and Refit builds the implementation from an `HttpClient` at
run time. `Hardened.Refit.Testing` is the test side: `[assembly: RefitTesting]` makes every Refit
interface a test parameter, and the same `Returns<T>()` asserts a call through it. The setting that
makes that whole is `returnIApiResponse` — Refitter's `--use-api-response` — which declares every
operation `Task<IApiResponse<T>>`, the envelope that carries the status and the headers back beside
the body, so nothing throws and a 201's `Location` is simply on the response. A method declared
`Task<T>` throws for a refusal, which reads the same way, and returns the body alone for a success —
and `Returns` refuses that success by name, because its status is gone. Refit has no error mapping,
so an error body arrives as text and is read as the expectation's type argument, the `Problem` in
`NotFound<Problem>`, through the client's own `RefitSettings`. The Refitter tool and the `Refit`
package are pinned separately and bumped together by hand; Refitter does not report the version it
writes for, so nothing checks the pair the way `HTPL003` checks Kiota's.

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

## Next

- [Typed clients](/guide/testing-clients): the client as a test parameter, and the transport underneath it
- [Asserting a response](/guide/testing-responses): `Returns<T>()`, `ReturnsStatus<T>()` and `LastResponse`
- [The OpenAPI document](/guide/openapi-document): what the exported file contains and how to shape it
- [Project templates](/guide/project-templates): `--client` scaffolds all of this; `none` leaves it out
