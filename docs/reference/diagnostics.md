# Diagnostics

This page lists every diagnostic code that Hardened's generators, build tasks, package targets and
project templates report. The `DM` and `VM` codes come from two libraries compiled into Hardened's
generators. [Prefixes](#prefixes) links to their own lists.

The build reports `HRDR010` for this handler, added to a `dotnet new hardened-web -n Todos` project
as `src/Todos/SearchController.cs`:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class SearchController
{
    [Get("/search")]
    public string Find(string title) => title;
}
```

`dotnet build` prints the code as a warning:

```console
$ dotnet build src/Todos
CSC : warning HRDR010: Parameter 'title' of 'SearchController.Find' is read from the request body, and a GET carries none, so a request that sends no body is refused before the handler runs and the published document gives the operation a body it should not have. Bind 'title' with [FromQueryString] or [FromHeader], mark it [FromServices] if it is a service, or suppress HRDR010 if this operation deliberately reads a body from a GET.
```

A build prints each code with its severity and its id, as in `warning HRDR010`, `error HRDR009` and
`info HRDF003`. A generator diagnostic with no source position prints as `CSC : warning <id>`. One
with a source position prints the file, line and column first. A build task diagnostic about a
description, such as the OpenAPI contract, prints the description's path. Other build task
diagnostics print the package's `.targets` file.

An enum value in an attribute on a `[HardenedModule]` class can fail the build with `CS1503` or
`CS0266` in the generated module code. [Attributes](/reference/attributes) lists those values.

## Suppressing or escalating a code

`<NoWarn>` in the project that reports a code removes it. With this block in
`src/Todos/Todos.csproj`, the build above reports no warnings:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);HRDR010</NoWarn>
</PropertyGroup>
```

A `.globalconfig` file sets the severity of any generator diagnostic to `none`, `warning` or
`error`. The file holds `is_global = true` and a `dotnet_diagnostic.<id>.severity` line. This
`src/Todos/.globalconfig` removes `HRDR010`:

```ini
is_global = true

dotnet_diagnostic.HRDR010.severity = none
```

The build picks up a file named `.globalconfig` in the project's folder or a folder above it.

This table shows what each setting does to each kind of diagnostic. The Reported by column of each
code table below names the kind.

| Setting | Generator diagnostic at a source position | Other generator diagnostics | Build task and package targets diagnostics |
|---|---|---|---|
| `<NoWarn>$(NoWarn);ID</NoWarn>` | Removes it, warning or error | Removes it, warning or error | Removes a warning. An error stays |
| `.globalconfig` with `dotnet_diagnostic.ID.severity` | Sets the severity | Sets the severity | No effect |
| `.editorconfig` `[*.cs]` with `dotnet_diagnostic.ID.severity` | Sets the severity | No effect | No effect |
| `#pragma warning disable ID` | Removes it | No effect | No effect |
| `<WarningsAsErrors>$(WarningsAsErrors);ID</WarningsAsErrors>` | Makes the warning an error | Makes the warning an error | Makes the warning an error |
| `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` | Makes every warning an error | Makes every warning an error | No effect |
| `<MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>` | Makes every warning an error | Makes every warning an error | Makes every warning an error |
| `<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>` | No effect | No effect | No effect, except `HSMT011` |

The generator diagnostics at a source position are `HOAG031`, `HOAG032`, `HRDR014`, `HRDV003`,
`HRDV004`, `HRDV005`, and the `DM` and `VM` codes. Each points at a position in a source file of the
project. `HAUTH001` and `HRDR013` print a file and line. Neither `#pragma` nor `.editorconfig`
reaches them.

A build under `TreatWarningsAsErrors` succeeds when its only warnings come from a build task or
package targets. `<WarningsNotAsErrors>` naming an id keeps that code a warning under
`TreatWarningsAsErrors`.

A category rule, `dotnet_analyzer_diagnostic.category-<category>.severity`, has no effect on any of
these codes. A diagnostic of severity Info prints only at detailed verbosity, `dotnet build -v:d`.

## Properties that change a code

Each of these build properties changes the codes in its row:

| Property | Code | Effect |
|---|---|---|
| `HardenedAmbiguousRoutes` | `HRDR001` | Its severity: `error`, the default, `warning`, `info` or `suggestion`, `none` or `hidden`. Any other value is `error`. |
| `HardenedSmithyPinCliVersion` | `HSMT011` | `true` makes it an error. It defaults to `ContinuousIntegrationBuild`. |
| `HardenedSkipCSharpAuthorVersionCheck` | `HARDENED001` | `true` turns the check off. |
| `AutoGenerateEntryPoint` | `HRDGF002` | `false` turns the check off. |
| `HardenedResponseModel` | `HOAT026`, `HSMT026` | `Standard` reports it. |

This project file sets `HRDR001` to a warning:

```xml
<PropertyGroup>
  <HardenedAmbiguousRoutes>warning</HardenedAmbiguousRoutes>
</PropertyGroup>
```

With `HRDR001` lowered by this property, by `<NoWarn>` or by a `.globalconfig`, the build fails with
`CS0152` in the generated routing table. The message is
`The switch statement contains multiple cases with the label value '"GET"'`.

## Prefixes

The prefix of a code names what reports it.

| Prefix | Reported by | Package |
|---|---|---|
| `HOAG` | The generators, for a handler they cannot write, and the specification-first generator, for described operations and the `[Handler]` classes that implement them | `Hardened.Web.SourceGenerator`, `Hardened.Function.SourceGenerator`, `Hardened.Idl.SourceGenerator` |
| `HAUTH` | The web generator, for authorization | `Hardened.Web.SourceGenerator` |
| `HRDR` | The routing generators, and the library generator for entry points and generator references | `Hardened.Web.SourceGenerator`, `Hardened.Idl.SourceGenerator`, `Hardened.Library.SourceGenerator` |
| `HRDW` | The web generator, for handler declarations | `Hardened.Web.SourceGenerator` |
| `HRDRM` | The web generator, for response cases | `Hardened.Web.SourceGenerator` |
| `HRDT` | The web generator, for `[Throws<T>]`, and the testing package's targets | `Hardened.Web.SourceGenerator`, `Hardened.Shared.Testing` |
| `HRDSC` | The web generator, for authentication scheme attributes | `Hardened.Web.SourceGenerator` |
| `HRDV` | The validation generator, and the web generator for handler parameters | `Hardened.Validation.SourceGenerator`, `Hardened.Web.SourceGenerator` |
| `HRDOA` | The web generator, for the OpenAPI document, and the build task that writes it to a file | `Hardened.Web.SourceGenerator` |
| `HRDF` | The library generator, for trigger adapters, and the function generator, for test façades | `Hardened.Library.SourceGenerator`, `Hardened.Function.SourceGenerator` |
| `HRDAZ` | The Azure Functions generator, and the Azure runtime's targets | `Hardened.Azure.Functions.SourceGenerator`, `Hardened.Azure.Functions.Runtime` |
| `HRDGF` | The Cloud Functions generator, and the Cloud Functions runtime's targets | `Hardened.Gcp.Functions.SourceGenerator`, `Hardened.Gcp.Functions.Runtime` |
| `HOAT` | The OpenAPI build task and its targets | `Hardened.OpenApi.SourceGenerator` |
| `HSMT` | The Smithy build tasks and their targets | `Hardened.Smithy.SourceGenerator` |
| `HTPL` | Checks the templates write into the projects they scaffold | `Hardened.Templates` |
| `HSTATIC` | The static content build task and its targets | `Hardened.Web.StaticContent` |
| `HARDENED` | The source package's version check | `Hardened.SourceGenerator` |
| `HardenedException` | Any Hardened generator that throws. The id has no number | Every generator package |
| `DM` | DependencyModules, inside the library generator | `Hardened.Library.SourceGenerator` |
| `VM` | ValidationModules, inside the validation generator | `Hardened.Validation.SourceGenerator` |

`HOAT` and `HSMT` share one numbering. `HRDOA` shares it for the codes of the build task that
writes the served document to a file. [HOAT and HSMT](#hoat-and-hsmt) lists these codes by number.

DependencyModules lists the `DM` codes in its
[diagnostics reference](https://ipjohnson.github.io/DependencyModules/reference/diagnostics).
ValidationModules lists the `VM` codes in its
[diagnostics reference](https://ipjohnson.github.io/ValidationModules/reference/diagnostics).

## HOAG

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HOAG001` | Warning | Writing the handler for a described operation threw. The message carries the exception's message | Generator | None |
| `HOAG002` | Warning | A model file the build task wrote, `*.openapi-model.txt`, cannot be read | Generator | [Generating from OpenAPI](/guide/openapi) |
| `HOAG010` | Warning | A handler parameter's type does not resolve for the generator. That handler is not generated, and the project's other handlers are | Generator | [Route links](/guide/route-links) |
| `HOAG020` | Error | A described operation answers `text/html` with a model, and its implementation declares no `[Output<T>]` | Generator | [Views](/guide/views) |
| `HOAG030` | Warning | No class carrying `[Handler]` implements a service the description declares. The service's routes exist and fail at request time | Generator | [Generating from OpenAPI](/guide/openapi) |
| `HOAG031` | Warning | A class carrying `[Handler]` names no service the description declares in its base list | Generator | [Generating from OpenAPI](/guide/openapi) |
| `HOAG032` | Warning | A `[Handler]` class, or one of its methods, carries `[RawResponse]`, `[Throws<T>]`, `[Tag]` or `[Server]`. None of them changes a described operation | Generator | [Generating from OpenAPI](/guide/openapi) |

## HAUTH

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HAUTH001` | Warning | The project's module class carries `[RequireAuthorization]`, and a handler compiled in the project carries no authorization attribute and no `[AllowAnonymous]`, on its method or its class | Generator | [Authorization](/guide/authorization) |

`[AllowAnonymous]` on the handler's method or on its class clears `HAUTH001`. So does an attribute
that implements `IAuthorizeAttribute`.

## HRDR

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HRDR001` | Error, or the severity `HardenedAmbiguousRoutes` names | Two routes under one method match the same paths and differ only in what one token accepts: a constraint, or `{name}` beside `{*name}` | Generator | [Routing](/guide/routing) |
| `HRDR002` | Error | A route uses a brace form the build does not compile, or names a constraint nothing declares | Generator | [Routing](/guide/routing) |
| `HRDR003` | Error | A `[RouteConstraint]` method is not `static bool` taking one `ReadOnlySpan<char>` | Generator | [Routing](/guide/routing) |
| `HRDR004` | Error | A project holds more than one `[HardenedModule]` class | Generator | [Modules](/guide/modules) |
| `HRDR005` | Error | A route token binds no parameter, and the parameter meant for it is read from the request body: its name differs from the token only by case, or the method is GET, HEAD, OPTIONS or TRACE | Generator | [Routing](/guide/routing) |
| `HRDR006` | Error | A project declares routes and no routing generator compiles them | Generator | [From scratch](/guide/from-scratch) |
| `HRDR007` | Error | A parameter typed as a service class is read from the request body. The class carries `[SingletonService]`, `[ScopedService]` or `[TransientService]`, or each of its public constructors takes an interface | Generator | [Registering services](/guide/services) |
| `HRDR008` | Error | Two routing generators compile one project, such as `Hardened.Web.SourceGenerator` and the one `Hardened.OpenApi.SourceGenerator` brings | Generator | [Generating from OpenAPI](/guide/openapi) |
| `HRDR009` | Error | More than one parameter binds from the request body | Generator | [Parameter binding](/guide/parameter-binding) |
| `HRDR010` | Warning | A parameter binds from the body of a GET, HEAD, OPTIONS or TRACE handler | Generator | [Parameter binding](/guide/parameter-binding) |
| `HRDR011` | Error | A handler answers with `byte[]` or `Stream`, bare or as the success case of a response set, and carries no `[Produces]` | Generator | [Content negotiation](/guide/content-negotiation) |
| `HRDR012` | Warning | A handler returns a model and declares a media type that nothing in the compilation or its references writes | Generator | [Content negotiation](/guide/content-negotiation) |
| `HRDR013` | Warning | A route attribute sits on an interface member. No route is compiled for it | Generator | [Routing](/guide/routing) |
| `HRDR014` | Error | A lambda route registration the build cannot read: the handler is not a lambda written in the call, or the verb is not a constant | Generator | [Registered routes](/guide/registered-routes) |

The `HRDR004` message says to move shared handlers into a `[WebLibrary]` project. Nothing reads the
attribute. Handlers that several applications share go in a project with its own
`[HardenedModule]` class. Each application imports that module, as `src/Todos.Host` imports
`src/Todos` in the `hardened-web` template.

## HRDW

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HRDW002` | Error | A handler binds a `[FromForm]` parameter and a request body parameter | Generator | [Forms and files](/guide/forms) |
| `HRDW003` | Error | A handler declares `[Compress]` more than once: on the method and on its class, or both `[Compress]` and `[Compress<T>]` | Generator | [Compression](/guide/compression) |
| `HRDW004` | Error | A handler that does not return `IAsyncEnumerable<T>` carries `[ServerSentEvents]`, or `[Produces("text/event-stream")]` on its method or its class | Generator | [Streaming responses](/guide/streaming) |
| `HRDW005` | Warning | A handler declares `[CacheResponse]`, and the application module applies `[KestrelRuntime]` or `[AspNetCoreRuntime]` and registers no response cache store | Generator | [Response caching](/guide/response-caching) |
| `HRDW006` | Error | A `[Timeout]` that covers a handler declares zero milliseconds or less | Generator | [Request timeouts](/guide/request-timeouts) |
| `HRDW007` | Error | A `[FromQueryString]` or `[FromForm]` model cannot be built from fields | Generator | [Parameter binding](/guide/parameter-binding) |
| `HRDW008` | Error | An `IFormFile` parameter is bound from anywhere but `[FromForm]` | Generator | [Forms and files](/guide/forms) |

## HRDRM

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HRDRM003` | Error | A response case is `object` or `dynamic` | Generator | [Declared responses](/guide/responses) |
| `HRDRM004` | Error | Two response cases at different statuses, where one type is assignable to the other | Generator | [Declared responses](/guide/responses) |

## HRDT

Two diagnostics share the id `HRDT001`. The web generator reports one as an error. The targets of
the testing package report the other as a warning.

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HRDT001` | Error | `[Throws<T>]` names a type without `[HttpStatus]` and states no status | Generator | [Declared responses](/guide/responses) |
| `HRDT001` | Warning | A test project references `Hardened.Shared.Testing` and neither `Hardened.Shared.Testing.xUnit` nor `Hardened.Shared.Testing.NUnit` | Package targets | [Writing a test](/guide/testing) |

## HRDSC

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HRDSC001` | Warning | `[HttpAuthenticationScheme]`, `[ApiKeyAuthenticationScheme]` or `[OAuth2AuthenticationScheme]` sits on a handler method, a controller class or a module class, where nothing reads it | Generator | [Authentication](/guide/authentication) |

## HRDV

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HRDV002` | Error | Two validators claim one generated file. The message asks for a defect report | Generator | [Validation](/guide/validation) |
| `HRDV003` | Warning | `[Required]` sits on a member of a non-nullable value type, such as `int` | Generator | [Validation](/guide/validation) |
| `HRDV004` | Warning | A member's type declares constraints, and the member has no `[ValidateNested]` | Generator | [Validation](/guide/validation) |
| `HRDV005` | Error | A constraint on a handler parameter sets `When` or `Unless` | Generator | [Validation](/guide/validation) |
| `HRDV006` | Warning | A project declares constraints on a handler and does not reference `Hardened.Validation.SourceGenerator` | Generator | [Validation](/guide/validation) |

## HRDOA

The web generator reports `HRDOA001` to `HRDOA005` for the document that a code-first project
serves.

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HRDOA001` | Error | `<HardenedOpenApiVersion>` is not `3.0`, `3.1` or `3.2`, with or without a trailing `.0` | Generator | [The OpenAPI document](/guide/openapi-document) |
| `HRDOA002` | Warning | A handler streams its response, and the document version is below 3.2 | Generator | [The OpenAPI document](/guide/openapi-document) |
| `HRDOA003` | Warning | A module with `[Enable<OpenApiDocumentPublishing>]` declares no routes, so the document it serves has no paths | Generator | [The OpenAPI document](/guide/openapi-document) |
| `HRDOA004` | Error | Two handlers declare the same `[Operation]` id | Generator | [The OpenAPI document](/guide/openapi-document) |
| `HRDOA005` | Warning | Two different types publish under one schema name | Generator | [The OpenAPI document](/guide/openapi-document) |

The build task that writes that document to a file reports `HRDOA018`, `HRDOA019` and `HRDOA028` to
`HRDOA031`. [HOAT and HSMT](#hoat-and-hsmt) lists them with the `HOAT` and `HSMT` codes of the same
numbers.

## HRDF

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HRDF001` | Error | Handlers use a trigger that no referenced package binds, while a referenced package binds another trigger | Generator | [Triggers](/guide/triggers) |
| `HRDF002` | Warning | Two sources of one kind give the same test façade method name, so only one of them can be reached through the façade | Generator | [Testing functions](/guide/testing-functions) |
| `HRDF003` | Info | A bound adapter module serves no trigger the project uses | Generator | [Triggers](/guide/triggers) |

## HRDAZ

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HRDAZ001` | Error | Declared and never reported: every trigger the Azure Functions generator serves has a binding | Generator | None |
| `HRDAZ002` | Error | Two handlers give the same Azure function name, such as `[Queue("orders-new")]` and `[Queue("orders_new")]` | Generator | Azure [Overview](/azure/) |
| `HRDAZ003` | Error | A binding needs a setting that the module attribute on the application does not supply: `Subscription` for `[Topic]`, `Database` for `[Change]`, or `RetryCount` without `RetryDelay`, or the reverse | Generator | Azure [Overview](/azure/) |
| `HRDAZ004` | Error | A setting the binding reads is written on the module attribute as anything but a string literal, `true`, `false` or a whole number in digits. A constant in any class is refused, and so is `RetryCount = -1` | Generator | Azure [Overview](/azure/) |
| `HRDAZ010` | Error | An executable that is not a test project references `Hardened.Azure.Functions.Runtime` and not `Microsoft.Azure.Functions.Worker.Sdk` | Package targets | Azure [Overview](/azure/) |

The `HRDAZ004` message asks for an integer literal. The generator refuses `-1` with that message. A
`warning CS1998` in the generated `Application.AzureFunctions.cs` comes with `HRDAZ003` and
`HRDAZ004`. This output is from a project made with
`dotnet new hardened-function --host azure --trigger stream`:

```text
CSC : error HRDAZ004: Connection on [EventHubsModule] is written as global::Orders.Application.Connection, and the function metadata the host indexes needs its value at build. Write it as a string literal, an integer literal, or true or false.
CSC : error HRDAZ004: RetryCount on [EventHubsModule] is written as -1, and the function metadata the host indexes needs its value at build. Write it as a string literal, an integer literal, or true or false.
src/Orders/obj/Debug/net8.0/generated/Hardened.Azure.Functions.SourceGenerator/Hardened.Azure.Functions.SourceGenerator.AzureFunctionsSourceGenerator/Application.AzureFunctions.cs(23,63): warning CS1998: This async method lacks 'await' operators and will run synchronously. Consider using the 'await' operator to await non-blocking API calls, or 'await Task.Run(...)' to do CPU-bound work on a background thread.
```

## HRDGF

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HRDGF001` | Warning | A Cloud Functions assembly holds more than one application. The message names the entry types to choose from | Generator | Google Cloud [Web services](/gcp/web) |
| `HRDGF002` | Warning | An executable references `Hardened.Gcp.Functions.Runtime` and does not reference `Google.Cloud.Functions.Hosting` itself, and does not set `AutoGenerateEntryPoint` | Package targets | Google Cloud [Web services](/gcp/web) |

## HTPL

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HTPL001` | Error | The project was scaffolded with `hardened-web --host aws-lambda --response-model union` | Template | [Project templates](/guide/project-templates) |
| `HTPL002` | Error | The Kiota tool does not restore | Template | [Generated clients](/guide/clients) |
| `HTPL003` | Error | The Kiota tool and `KiotaBundleVersion` name different Kiota releases | Template | [Generated clients](/guide/clients) |
| `HTPL004` | Error | The Refitter tool does not restore | Template | [Generated clients](/guide/clients) |
| `HTPL005` | Error | The project was scaffolded with `hardened-function --host gcp --trigger stream` | Template | [Project templates](/guide/project-templates) |
| `HTPL006` | Error | The project was scaffolded with `hardened-function --host azure --trigger invoke` | Template | [Project templates](/guide/project-templates) |
| `HTPL007` | Error | The project was scaffolded with `hardened-web --host azure-functions --response-model union` | Template | [Project templates](/guide/project-templates) |
| `HTPL008` | Error | The project was scaffolded with `hardened-web --contract smithy` and `--serializer message-pack-named` or `message-pack-keyed` | Template | [Project templates](/guide/project-templates) |

Each of `HTPL001` and `HTPL005` to `HTPL008` is in a project only when the project was scaffolded
with the options in its row. The templates write the checks into the projects they scaffold:

| Written into | Codes |
|---|---|
| `Directory.Build.props` of a `hardened-web` project | `HTPL001`, `HTPL007`, `HTPL008` |
| `Directory.Build.props` of a `hardened-function` project | `HTPL005`, `HTPL006` |
| The Kiota client project | `HTPL002`, `HTPL003` |
| The Refitter client project | `HTPL004` |

Each check is an MSBuild `Error` task in that file, so no setting in the suppression table turns
one off.

## HSTATIC

`Hardened.Web.StaticContent` reports these codes while it builds the manifest of the files that a
`HardenedStaticContent` item names.

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HSTATIC000` | Error | The static content directory cannot be read | Build task | None |
| `HSTATIC001` | Error | The directory the item names does not exist | Build task | None |
| `HSTATIC002` | Error | A file in the directory resolves to a path outside it | Build task | None |
| `HSTATIC003` | Warning | A hidden file, or one that looks like a secret, is left out of the manifest and is not served | Build task | None |
| `HSTATIC004` | Warning | The directory is empty | Build task | None |
| `HSTATIC005` | Error | The fallback file is not in the directory | Build task | None |
| `HSTATIC006` | Error | `Hardened.Web.StaticContent.targets` was imported before the `HardenedStaticContent` items were declared | Package targets | None |
| `HSTATIC007` | Error | The manifest was not written. The message says to delete `obj/<configuration>/<framework>/staticcontent` and rebuild | Package targets | None |
| `HSTATIC008` | Error | A `.gz` or `.br` file does not decompress | Build task | None |
| `HSTATIC009` | Error | A `.br` file is in a build that Visual Studio's MSBuild runs, which cannot decompress Brotli | Build task | None |

## HARDENED

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HARDENED001` | Error | A project compiles the `Hardened.SourceGenerator` source package, with `PackageHardenedIncludeSource` set to `true`, and pins a CSharpAuthor version below 2.0.0. A prerelease or floating pin is not compared | Package targets | None |

## HardenedException

| Id | Severity | Reported when | Reported by | Page |
|---|---|---|---|---|
| `HardenedException` | Error | A Hardened generator threw while it wrote the source for one item. The message carries the exception | Generator | None |

The generator writes nothing for that item. Compiler errors follow for the code it should have
written. This output is from a project made with
`dotnet new hardened-function --host azure --trigger stream`, with the handler's
`[Stream("orders")]` changed to `[Stream("orders:eu")]`:

```text
CSC : error HardenedException: The generator threw and produced no source: ArgumentException: The hintName 'STREAM.orders:eu.FunctionHandler.cs' contains an invalid character ':' at position 13. (Parameter 'hintName') at Microsoft.CodeAnalysis.AdditionalSourcesCollection.Void Add(System.String, Microsoft.CodeAnalysis.Text.SourceText)
src/Orders/obj/Debug/net8.0/generated/Hardened.Function.SourceGenerator/Hardened.Function.SourceGenerator.FunctionLibrarySourceGenerator/Application.FunctionHandlers.cs(42,61): error CS0234: The type or namespace name 'OrderHandler_OnOrder_664' does not exist in the namespace 'Orders.Generated' (are you missing an assembly reference?)
```

## HOAT and HSMT

A number names the same condition under `HOAT` and `HSMT`. The number `026` names two conditions
under both.

For `<HardenedOpenApiOutput>`, a build task writes the served document to a file. That task reports
under `HRDOA` in a code-first project. It reports under `HOAT` in an OpenAPI-first project and under
`HSMT` in a Smithy-first one.

The OpenAPI and Smithy build tasks run only when their output under `obj` is missing, or when the
description, its item, a build property they read or the package changed since the last build. A
build that skips the tasks prints none of their warnings. `dotnet build --no-incremental` skips them
too. They run again after the model folder, such as `obj/Debug/net8.0/openapi`, is deleted.

Both `026` warnings print in one build of a project made with
`dotnet new hardened-web -n Todos --contract openapi --client none`. Its `contracts/todos.yaml` has
the `removeTodo` operation's parameter renamed from `id` to `Id`. The build runs as
`dotnet build src/Todos -p:HardenedResponseModel=Standard`. This listing shortens the package path:

```text
.../build/Hardened.OpenApi.SourceGenerator.targets(211,9): warning HOAT026: $(HardenedResponseModel) is 'Standard', which was renamed 'Throws' in 0.19.0. The mode selected is unchanged; write <HardenedResponseModel>Throws</HardenedResponseModel>.
src/Todos/contracts/todos.yaml : warning HOAT026: 'DELETE /todos/{id}' declares '{id}' and the operation declares no path parameter of that name - 'Id' differs from it only in case. The route still matches and the value is discarded, so the handler cannot read the segment that chose the resource. Declare the parameter, or take the token out of the path.
```

The same build then fails with compiler errors, because the scaffold's `TodoService` implements the
interface that the `Response` model generates.

### Reading the description

| Number | Severity | Reported when | Reported by | Codes and pages |
|---|---|---|---|---|
| `001` | Error | The file an item names does not exist | Build task | `HOAT001`: [Generating from OpenAPI](/guide/openapi). `HSMT001`: none |
| `002` | Error | The description cannot be read. For OpenAPI, also an `x-hardened-timeout` of zero or less. For Smithy, also a model with no service or no operation, a refused protocol, or a `HardenedSmithyServiceShapeId` the model does not declare | Build task | `HOAT002`: [Generating from OpenAPI](/guide/openapi). `HSMT002`: [Generating from Smithy](/guide/smithy) |
| `003` | Error | The description is declared as the wrong item: a `.yaml` file as `AdditionalFiles` with no `HardenedOpenApiSpec` item, or a `.smithy` file as `HardenedSmithyAst` | Package targets | `HOAT003`: [Generating from OpenAPI](/guide/openapi). `HSMT003`: [Generating from Smithy](/guide/smithy) |
| `004` | Error | A model file or a generated source file that the build task writes is missing. The message says to delete the model folder and rebuild | Package targets | `HOAT004`: [Generating from OpenAPI](/guide/openapi). `HSMT004`: [Generating from Smithy](/guide/smithy) |
| `005` | Error | The package's `.targets` file is imported by hand above the items that declare the descriptions | Package targets | `HOAT005` and `HSMT005`: none |
| `006` | Warning | The reader reports a problem in a description it could read. For OpenAPI, among others, scopes on a scheme that carries none. For Smithy, among others, a member narrowed to `decimal` or `long`, a trait it does not model, or a `@timeout` of zero or less | Build task | `HOAT006`: [Generating from OpenAPI](/guide/openapi). `HSMT006`: [Generating from Smithy](/guide/smithy) |
| `007` | Error | A slice keeps no operation | Build task | `HOAT007`: [Generating from OpenAPI](/guide/openapi). `HSMT007`: none |
| `008` | Warning | A slice removed a schema that is still referenced. The reference becomes `JsonElement` | Build task | `HOAT008` and `HSMT008`: none |
| `009` | Warning | A sliced description is embedded whole | Build task | `HOAT009`: [Generating from OpenAPI](/guide/openapi). `HSMT009`: none |
| `016` | Error | `UiUrl` is set without `PublishUrl` | Build task | `HOAT016` and `HSMT016`: [The OpenAPI document](/guide/openapi-document) |
| `017` | Error | `SourceUrl` is set and the description is not embedded | Build task | `HOAT017` and `HSMT017`: [The OpenAPI document](/guide/openapi-document) |
| `026` | Warning | `HardenedResponseModel` is `Standard`. The build uses `Throws` | Build task | `HOAT026` and `HSMT026`: none |

### The Smithy CLI and model items

`010` to `015` exist under `HSMT` only.

| Number | Severity | Reported when | Reported by | Codes and pages |
|---|---|---|---|---|
| `010` | Error | The Smithy CLI is on neither `PATH` nor `HardenedSmithyCliPath` | Build task | `HSMT010`: [Generating from Smithy](/guide/smithy) |
| `011` | Warning, or Error when `HardenedSmithyPinCliVersion` is `true` | The CLI's version is not `HardenedSmithyCliVersion`. An error also when `smithy --version` fails | Build task | `HSMT011`: [Generating from Smithy](/guide/smithy) |
| `012` | Error | The CLI refuses the model. One error for each finding | Build task | `HSMT012`: [Generating from Smithy](/guide/smithy) |
| `013` | Warning | The CLI succeeds and reports a warning | Build task | `HSMT013`: [Generating from Smithy](/guide/smithy) |
| `014` | Error | The CLI succeeds and writes no AST | Build task | `HSMT014`: [Generating from Smithy](/guide/smithy) |
| `015` | Error | The `HardenedSmithyModel` items name more than one `PublishUrl`, or more than one `UiUrl` | Package targets | `HSMT015`: [Generating from Smithy](/guide/smithy) |

### Checks on the model

| Number | Severity | Reported when | Reported by | Codes and pages |
|---|---|---|---|---|
| `020` | Warning | A schema declares a property named like the schema itself. The member is renamed, and the wire name is unchanged | Build task | `HOAT020` and `HSMT020`: [Generating from OpenAPI](/guide/openapi) |
| `021` | Warning | Two schemas still generate one type name after the build's own renaming | Build task | `HOAT021` and `HSMT021`: none |
| `022` | Warning | A `oneOf` with no discriminator has branches that its shapes cannot tell apart | Build task | `HOAT022` and `HSMT022`: [Generating from OpenAPI](/guide/openapi) |
| `023` | Error | An `enum` declares both string and numeric values | Build task | `HOAT023` and `HSMT023`: [Generating from OpenAPI](/guide/openapi) |
| `024` | Warning | A keyword or trait is declared, and the generated code does not enforce it. One warning for each keyword | Build task | `HOAT024` and `HSMT024`: [Generating from OpenAPI](/guide/openapi) |
| `026` | Warning | A path names a token that the operation declares no path parameter for | Build task | `HOAT026` and `HSMT026`: [Generating from OpenAPI](/guide/openapi) |
| `027` | Error | A reference names something the description does not declare | Build task | `HOAT027` and `HSMT027`: [Generating from OpenAPI](/guide/openapi) |
| `032` | Warning | An OpenAPI 3.1 or later document declares `nullable` | Build task | `HOAT032`: [Generating from OpenAPI](/guide/openapi). `HSMT032`: none |
| `033` | Error | Under `HardenedSerializer` `MessagePackKeyed`, a property has no `x-message-pack-index`, or two properties share one | Build task | `HOAT033`: [MessagePack](/guide/message-pack). `HSMT033`: none |
| `034` | Warning | A `oneOf` schema under a MessagePack serializer | Build task | `HOAT034`: [MessagePack](/guide/message-pack). `HSMT034`: none |

No build reports `HSMT032`, because only the OpenAPI reader records `nullable`.

### Writing the served document to a file

| Number | Severity | Reported when | Reported by | Codes and pages |
|---|---|---|---|---|
| `018` | Error | `<HardenedOpenApiOutput>` is set, and the assembly serves no document, or one the task cannot read | Build task | `HRDOA018`, `HOAT018` and `HSMT018`: [The OpenAPI document](/guide/openapi-document) |
| `019` | Error | `<HardenedOpenApiOutput>` is set, and the assembly serves more than one document | Build task | `HRDOA019`, `HOAT019` and `HSMT019`: [The OpenAPI document](/guide/openapi-document) |
| `028` | Error | The `<HardenedOpenApiOutput>` path does not end in `.json`, `.yaml` or `.yml` | Build task | `HRDOA028`, `HOAT028` and `HSMT028`: [The OpenAPI document](/guide/openapi-document) |
| `029` | Error | `<HardenedOpenApiOutputVersion>` is not `3.0` or `3.1`, with or without a trailing `.0` | Build task | `HRDOA029`, `HOAT029` and `HSMT029`: [The OpenAPI document](/guide/openapi-document) |
| `030` | Warning | `<HardenedOpenApiOutputVersion>` lowers the file, and an operation streams its response. One warning for each such operation | Build task | `HRDOA030`, `HOAT030` and `HSMT030`: [The OpenAPI document](/guide/openapi-document) |
| `031` | Warning | The served document puts more than one operation under one method at one path | Build task | `HRDOA031` and `HOAT031`: none. `HSMT031`: [Generating from Smithy](/guide/smithy) |

## Next

- [Packages](/reference/packages): the packages the prefixes come from
- [Generating from OpenAPI](/guide/openapi): the OpenAPI build and its codes
- [Generating from Smithy](/guide/smithy): the Smithy build and the Smithy CLI
- [The OpenAPI document](/guide/openapi-document): the served document and its export to a file
- [Validation](/guide/validation): validation, and where the `VM` codes come from
