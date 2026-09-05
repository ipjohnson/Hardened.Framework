# Generated clients

Hardened writes the OpenAPI document and Kiota writes the client. The `hardened-web` template
scaffolds a client project with no hand-written code, generated during the build from the
document the library exports, and tests that drive it through the application's own pipeline:

```csharp
[HardenedTest]
public async Task CreateTodo_AnswersCreatedWithALocation(TodosClient client) {
    var created = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
        .Returns<Created<ClientModels.Todo>>();

    Assert.Equal("ship it", created.Value.Title);
    Assert.Equal($"/todos/{created.Value.Id}", created.Location);
}
```

There is no Hardened package around any generator. The exported file is the deliverable, and the
same file feeds every other generator and language. [Typed clients](/guide/testing-clients)
covers the client as a test parameter and [Asserting a response](/guide/testing-responses) covers
`Returns<T>()`. This page is the client itself: where the document comes from, how the project is
built, and how a consumer uses it against a real service.

## The export

One MSBuild property, on the project whose build produces the document:

```xml
<PropertyGroup>
  <HardenedOpenApiOutput>openapi/Todos.json</HardenedOpenApiOutput>
</PropertyGroup>
```

After every compile the build writes the document the application serves at `/openapi.json`,
read out of the compiled assembly rather than a running application, so it works for a Lambda
function and for a library with no entry point. Code-first, the module that declares the routes
needs `[Enable<OpenApiDocumentPublishing>]`, and the build says so (`HRDOA018`) if the property
is set without it. Contract-first, every project that generated from a contract already carries
the document.

`HardenedOpenApiOutputVersion` lowers the written file to `3.0.0` or `3.1.0` for a reader that
refuses the 3.2 banner Hardened emits by default. NSwag's reader is 3.0-first, and Spectral's
`oas` ruleset stops at 3.1. A streaming operation loses its `itemSchema` on the way down, and the
build names each one (`030`). [The OpenAPI document](/guide/openapi-document#the-export) has the
whole vocabulary.

## The client project

`src/Todos.Client` has no hand-written code. Two targets in its project file do the work:

```xml
<PropertyGroup>
  <KiotaSpec>../Todos/openapi/Todos.json</KiotaSpec>
  <KiotaOutput>$(BaseIntermediateOutputPath)kiota/</KiotaOutput>
</PropertyGroup>

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
```

The scaffolded file carries a comment on each choice. In short:

- `RestoreKiota` runs `dotnet tool restore` inside the build, so a fresh clone builds with no
  instruction beyond `dotnet build`.
- Output goes under `obj/` and is added to the compilation inside a target, because the SDK's
  `**/*.cs` glob is evaluated before any target runs. `kiota-lock.json` is the up-to-date stamp,
  so an unchanged document skips Kiota.
- A project reference to the library with `ReferenceOutputAssembly="false"` orders the build and
  nothing else. The client's only dependency is `Microsoft.Kiota.Bundle`, and it targets `net8.0`
  whatever the library targets.
- The tool in `.config/dotnet-tools.json` and `KiotaBundleVersion` in `Directory.Packages.props`
  have to agree, because the generated code and the runtime come from matching Kiota releases. A
  mismatch is `HTPL003`, naming both files. `HTPL002` is the tool failing to restore, which on a
  fresh machine means no network.

Nothing generated is committed; the document is. In CI, build and then check the file is current:

```bash
dotnet build
git diff --exit-code src/Todos/openapi
```

## Against a real service

The same project is what other teams consume. Two things change: the base URL is real, and so is
the credential.

### Ship it

The client project is packable and its only dependency is the Kiota bundle:

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
redirect, parameter-name decoding and the rest. The test harness deliberately does not use it,
because a test asserting a 429 or a 308 wants what the pipeline answered rather than what the
middleware made of it. In production it is what you want.

`BaseUrl` is required by Kiota. A code-first document with no
[`[Server]`](/guide/openapi-document#what-else-reaches-the-document) has no `servers` entry, so
Kiota warns on every generation and this is the line that settles it.

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

`AllowedHostsValidator` stops the token going out on a redirect to somewhere else.
`ApiKeyAuthenticationProvider` is the equivalent for a key in a header or query value. These are
Kiota's types, and [Kiota's authentication reference](https://learn.microsoft.com/openapi/kiota/authentication)
has the rest of them.

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

A named client rather than `AddHttpClient<TodosClient>()`, because the typed-client overload
constructs `TodosClient` from an `HttpClient` and a Kiota client's constructor takes an
`IRequestAdapter`. `IHttpClientFactory` gives the client socket reuse and DNS refresh.
`AddStandardResilienceHandler` comes from `Microsoft.Extensions.Http.Resilience`. Use it or
Kiota's own middleware, not both, or a failed request is retried by each in turn.

Nothing about the client knows whether its `HttpClient` reaches a socket or the in-process
pipeline. The client a consumer ships with is the client the service's own tests drive.

## What Kiota does with a Hardened document

Kiota reads the 3.2 banner Hardened emits by default, and its .NET libraries strip a vendor prefix
when picking a parser, so the error mapping keeps working if the error envelope moves to
`application/problem+json`. For the template's four operations under the default response model:

| The document declares | Kiota generates | The consumer sees |
|---|---|---|
| 200/400/404 on `GET /todos/{id}`, 201/409 on `POST /todos`, 204/400/404 on `DELETE /todos/{id}`, with `NotFound`, `Conflict` and `RequestValidationError` bodies | Request builders per path, models, and an exception type per error schema mapped to its statuses | `await client.Todos[1].GetAsync()`; a declared 404 throws `NotFound`, a 400 throws `RequestValidationError`, both carrying the body |

Three things no scaffold gets from Kiota:

- A client signature that mirrors `Response<Todo, NotFound>`. Kiota's model is exceptions.
- A typed method for a streaming operation. Kiota ignores `text/event-stream` and does not read
  `itemSchema`, so an application that adds one gets a client without it.
- A composed type for two success cases at one status without a discriminator, which `HOAT022`
  already warns about on the server side.

### Where the service and the document disagree

These are the service's shape rather than Kiota's, found by generating a client against it.

In throws mode an OpenAPI service answers a `null` return with the document's `Problem` and no
`detail`. That deserializes, so the client throws the typed exception either way, and only the
message is empty. A Smithy `@error` shape answered by `null` carries its `message` filled with the
status's reason phrase, so the client throws the typed error there too. A handler with something
to say throws the error with a body of its own.

A Smithy model on the AWS JSON protocol produces a document no client can be generated from.
Every operation is `POST /`, told apart by a header, so the document repeats the `post` key under
one path. The export carries it faithfully, and Microsoft.OpenApi's reader refuses it. A Smithy
service that wants a generated client needs `@http` bindings that give each operation its own
method and path.

## Other generators

Generator-specific code is confined to the client project's build target and package references,
and to the generator's testing package. A client whose constructor takes exactly one `HttpClient`
is built for a test with no package and no factory at all; see
[How a client is built](/guide/testing-clients#how-a-client-is-built).

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

The client project's generate target becomes `dotnet tool run nswag run nswag.json`, and the test
project needs no factory, because the generated class takes an `HttpClient`.

**Refitter.** `--client refit` scaffolds it. The client project restores the Refitter tool and
runs it over the exported document under the settings in its `.refitter` file, writing a Refit
interface and its models into `obj/`, and Refit builds the implementation from an `HttpClient` at
run time. `returnIApiResponse` in that file declares every operation `Task<IApiResponse<T>>`, the
envelope that carries the status and the headers back beside the body, which is what
`Returns<T>()` reads. The Refitter tool and the `Refit` package are pinned separately and bumped
together by hand; nothing checks the pair the way `HTPL003` checks Kiota's.

**openapi-generator.** `openapi-generator-cli generate -g csharp -i src/Todos/openapi/Todos.json
-o clients/csharp`. It needs a Java runtime, and its generated project is a solution of its own
rather than one target, so treat it as a separate build.

**Other languages.** The same file. `kiota generate --language TypeScript --openapi
src/Todos/openapi/Todos.json --output clients/typescript`, and the same for Java, Go, Python, PHP
and Ruby, each into that language's own package manifest and toolchain.

## More than one language: the workspace

Kiota's workspace turns one document into several clients from one configuration. The workspace
commands have been in preview since Kiota 1.12 and sit behind a flag. Their model assumes
committed output: `apimanifest.json` records a hash per client, so a team adopting it usually
also commits the generated clients with the same `git diff --exit-code` that guards the document.
Every output path has to live inside the workspace root.

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
and each further language is one more `kiota client add`.

## Next

- [Typed clients](/guide/testing-clients): the client as a test parameter, and the transport underneath it
- [Asserting a response](/guide/testing-responses): `Returns<T>()`, `ReturnsStatus<T>()` and `LastResponse`
- [The OpenAPI document](/guide/openapi-document): what the exported file contains and how to shape it
