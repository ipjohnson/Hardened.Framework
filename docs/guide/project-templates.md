# Project templates

The `Hardened.Templates` package installs three `dotnet new` templates: `hardened-web`,
`hardened-function` and `hardened-library`. Options on `dotnet new` choose what the template
writes.

```bash
dotnet new hardened-web -n Todos --host aws-lambda --contract openapi
```

The command writes the same four projects as the default `hardened-web` scaffold, with an AWS
Lambda host and an OpenAPI contract. [Getting started](/guide/getting-started) takes the default
scaffold from install to passing tests. This page covers the three templates and their options.

The three templates write these solutions:

| Template | Writes |
|---|---|
| `hardened-web` | An HTTP API: a library for the handlers, a host project, a client project and a test project |
| `hardened-function` | A function for one trigger on one cloud, and its tests |
| `hardened-library` | A module that an application imports with one attribute, and its tests |

The scaffolded tests pass when `dotnet test` runs in the solution directory.

## Install the templates

`dotnet new install Hardened.Templates` installs the newest version from nuget.org. Every version is
a prerelease version. The command still finds the newest one.

`dotnet new install Hardened.Templates::<version>` installs a specific version:

```bash
dotnet new install Hardened.Templates::0.0.0-HARDENED-VERSION
```

The .NET 8 SDK rejects the form `Hardened.Templates@<version>` with "is not supported" and exit
code 106. The .NET 10 SDK accepts both forms. It prints a deprecation notice for the `::` form.

The version of the template package is the Hardened version that the templates write into each new
project. [Package versions](#package-versions) describes how a project pins it.

## hardened-web

`dotnet new hardened-web -n Todos` writes a solution into a directory named `Todos`. The name given
with `-n` names the projects and two classes. `-n Todos` gives the library module `TodosLibrary`
and the JSON context `TodosJsonContext`. In the class names, the template drops any character that
a C# identifier cannot hold.

With the default options, the solution holds these projects:

| Project | Contents |
|---|---|
| `src/Todos` | The handlers, the models, the services and the library module `TodosLibrary` |
| `src/Todos.Host` | `Application.cs` and `Program.cs` for the chosen host |
| `src/Todos.Client` | A client that the build generates from the OpenAPI document |
| `tests/Todos.Tests` | Tests that send requests to `TodosLibrary` in process |

The first build writes the application's OpenAPI document to `src/Todos/openapi/Todos.json`. The
client project generates the client from that file. `dotnet new` does not write the file.

The root `.config/dotnet-tools.json` pins the Kiota tool, `microsoft.openapi.kiota`. The client
project restores and runs it during the build.

`src/Todos.Host/Properties/launchSettings.json` holds a launch profile that opens a browser at
`http://localhost:5080/docs`. The application serves the reference page at `/docs` only in the
`development` environment. The [environment](/guide/environments) is `development` when
`HARDENED_ENVIRONMENT` is not set. With `HARDENED_ENVIRONMENT=production`, `/docs` answers 404 and
`/openapi.json` answers 200.

`hardened-web` takes these options, in addition to the
[options on every template](#options-on-every-template):

| Option | Short name | Values | Default | Selects |
|---|---|---|---|---|
| `--host` | `-ho` | `kestrel`, `aspnet`, `aws-lambda`, `cloud-run`, `azure-functions` | `kestrel` | Where the application runs. See [Hosts](/guide/hosts) |
| `--contract` | `-c` | `code`, `openapi`, `smithy` | `code` | Where the API contract lives. See [Generating from OpenAPI](/guide/openapi) and [Generating from Smithy](/guide/smithy) |
| `--response-model` | `-rm` | `response`, `throws`, `union` | `response` | How a handler declares its statuses. See [Declared responses](/guide/responses) |
| `--client` | `-cl` | `kiota`, `refit`, `none` | `kiota` | The generated client. See [Generated clients](/guide/clients) |
| `--serializer` | `-s` | `json`, `message-pack-named`, `message-pack-keyed` | `json` | MessagePack as a second representation beside JSON. See [MessagePack](/guide/message-pack) |
| `--openapi-ui` | None | `true`, `false` | `true` | A reference page at `/docs`. See [The OpenAPI document](/guide/openapi-document) |

`--host` changes the host project. `src/Todos` and `src/Todos.Client` are the same for all five
hosts. Depending on the host, `--host` also changes the test project, `Directory.Packages.props`,
`README.md` and `AGENTS.md`. Each host adds files:

| `--host` | Host attribute in the test project | Added files |
|---|---|---|
| `kestrel` | `[assembly: KestrelTesting]` | `tests/Todos.Tests/TodosSocketTests.cs` |
| `aspnet` | `[assembly: AspNetCoreTesting]` | `tests/Todos.Tests/TodosSocketTests.cs` |
| `aws-lambda` | None | `src/Todos.Host/.config/dotnet-tools.json`, which pins the AWS Lambda Test Tool |
| `cloud-run` | `[assembly: KestrelTesting]` | `Dockerfile`, `.dockerignore`, `tests/Todos.Tests/TodosSocketTests.cs` |
| `azure-functions` | `[assembly: AzureFunctionsWebTesting]` | `src/Todos.Host/host.json`, `src/Todos.Host/local.settings.json`. There is no launch profile |

`TodosSocketTests.cs` runs the application on a real socket. The other tests send requests in
process.

The remaining option values change the default scaffold:

| Option value | What changes |
|---|---|
| `--contract openapi` | Adds `src/Todos/contracts/todos.yaml`, declared as a `HardenedOpenApiSpec` item, and `TodoService.cs`, which implements the generated interface. Removes `TodoController.cs` and `TodosJsonContext.cs` |
| `--contract smithy` | The same, with `src/Todos/contracts/todos.smithy` declared as a `HardenedSmithyModel` item. The build needs the Smithy CLI |
| `--response-model throws` | `TodoController.cs` and the tests |
| `--response-model union` | Every project targets `net11.0`. `global.json` pins the .NET SDK `11.0.100-preview.7.26381.103`. `src/Todos` sets `LangVersion` to `preview` |
| `--client refit` | `src/Todos.Client` holds a Refit interface that Refitter generates. The tool manifest pins `refitter`. The tests use `[assembly: RefitTesting]` |
| `--client none` | No `src/Todos.Client` and no `.config/dotnet-tools.json`. The build does not write the document to a file. The tests send requests with `ITestWebApp` |
| `--serializer message-pack-named` or `message-pack-keyed` | Adds the `Hardened.Requests.Serializers.MessagePack` package, `[MessagePackSerializerLibrary]` and `[JsonErrorBodies]` on `TodosLibrary`, and `[Produces(KnownContentType.Json, MessagePackContentType.Value)]` on each route. With `--client refit` it also adds Liquid templates and `MessagePackContentSerializer.cs` to the client, and `MessagePackClientFactory.cs` to the tests |
| `--openapi-ui false` | Removes `[HardenedOpenApiUi]` from `Application` in a code-first project, or `<UiUrl>` and `<UiEnvironments>` from the contract item. Removes the launch profile |

`/openapi.json` does not depend on `--openapi-ui`. `[Enable<OpenApiDocumentPublishing>]` on the
library module serves the document. `--openapi-ui false` leaves the module unchanged.

## hardened-function

`dotnet new hardened-function -n OrderIntake` writes one project, `src/OrderIntake`, and a test
project, `tests/OrderIntake.Tests`. There is no host project. `src/OrderIntake` holds the handler
class `OrderHandler`, its models and services, `Application.cs` and `Program.cs`. `Program.cs` is
the entry point for the chosen cloud. The project references the one adapter package that serves
the trigger on the chosen cloud.

`hardened-function` takes these options, in addition to the
[options on every template](#options-on-every-template):

| Option | Short name | Values | Default | Selects |
|---|---|---|---|---|
| `--trigger` | `-tr` | `invoke`, `queue`, `topic`, `timer`, `change`, `stream`, `blob` | `invoke` | The trigger attribute on the handler. See [Triggers](/guide/triggers) |
| `--host` | `-ho` | `aws`, `gcp`, `azure` | `aws` | AWS Lambda, Google Cloud Run or Azure Functions |

Each `--trigger` value writes one method on the handler class:

| `--trigger` | Handler method |
|---|---|
| `invoke` | `[HardenedFunction]` on `public OrderAccepted Process(Order order)` |
| `queue` | `[Queue("orders")]` on `public void OnOrder(Order order)` |
| `topic` | `[Topic("orders")]` on `public void OnOrder(Order order)` |
| `timer` | `[Timer("nightly")]` on `public void OnNightly()` |
| `change` | `[Change("orders")]` on `public void OnOrderChanged(Order order)` |
| `stream` | `[Stream("orders")]` on `public void OnOrder(Order order)` |
| `blob` | `[Blob("uploads")]` on `public void OnUpload(Upload upload)` |

`OrderHandler.cs` is the same for every `--host`. `--host` changes `Application.cs`, `Program.cs`,
the package references and the testing attributes in the test project. Each cloud adds files:

| `--host` | Testing attributes in the test project | Added files |
|---|---|---|
| `aws` | `[assembly: FunctionTesting]`, `[assembly: LambdaTesting]` | `.config/dotnet-tools.json`, which pins the AWS Lambda Test Tool |
| `gcp` | `[assembly: FunctionTesting]`, `[assembly: CloudRunTesting]`, `[assembly: WebTesting]` | `Dockerfile`, `.dockerignore` |
| `azure` | `[assembly: FunctionTesting]`, `[assembly: AzureFunctionsTesting]` | `src/OrderIntake/host.json`, `src/OrderIntake/local.settings.json` |

The cloud overviews for [AWS](/aws/), [Google Cloud](/gcp/) and [Azure](/azure/) cover running and
deploying a function.

## hardened-library

`dotnet new hardened-library -n Acme.Greeting` writes `src/Acme.Greeting` and
`tests/Acme.Greeting.Tests`. The module class is the name without the characters that a C#
identifier cannot hold, plus `Library`. `-n Acme.Greeting` gives `AcmeGreetingLibrary`. The module
has no host attribute. The library project references only `Hardened.Shared.Runtime` and
`Hardened.Library.SourceGenerator`.

The generator writes the attribute class `AcmeGreetingLibraryAttribute`. An application imports the
library by putting `[AcmeGreetingLibrary]` on its application module:

```csharp
using Acme.Greeting;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[AcmeGreetingLibrary]
public partial class Application;
```

To serve HTTP routes from the library, add `[HardenedWebModule]` to the module class. The attribute
is in the `Hardened.Web.Runtime.DependencyInjection` namespace:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Acme.Greeting;

[HardenedModule]
[HardenedWebModule]
public partial class AcmeGreetingLibrary;
```

The library also needs the `Hardened.Web.Runtime` and `Hardened.Web.SourceGenerator` packages. The
template's `Directory.Packages.props` has no version for them. A reference without a version fails
the restore with `NU1010`. Add both `PackageVersion` lines to `Directory.Packages.props`:

```xml
<PackageVersion Include="Hardened.Web.Runtime" Version="$(HardenedVersion)" />
<PackageVersion Include="Hardened.Web.SourceGenerator" Version="$(HardenedVersion)" />
```

Reference both packages in `src/Acme.Greeting/Acme.Greeting.csproj`:

```xml
<PackageReference Include="Hardened.Web.Runtime" />
<PackageReference Include="Hardened.Web.SourceGenerator" />
```

`GreetingController` declares a route in the library:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Acme.Greeting;

public class GreetingController
{
    [Get("/greeting/{name}")]
    public string Greet(IGreetingService greetings, string name) => greetings.Greet(name);
}
```

The application that imports the library serves the route:

```http
GET /greeting/world

HTTP/1.1 200 OK
Content-Type: application/json

"Hello, world!"
```

## Files in every solution

All three templates write these files:

| File | Contents |
|---|---|
| `global.json` | Pins the .NET SDK `8.0.401` with `rollForward` set to `latestFeature`. `--response-model union` pins `11.0.100-preview.7.26381.103` instead |
| `nuget.config` | Clears the package sources and adds nuget.org |
| `Directory.Build.props` | The target framework `net8.0` (`net11.0` with `--response-model union`), nullable reference types, implicit usings and `EmitCompilerGeneratedFiles` |
| `Directory.Packages.props` | Every package version. The Hardened packages use `$(HardenedVersion)` |
| `README.md` | How the solution builds, runs and tests, written for the options chosen |
| `AGENTS.md` | Rules and traps for anyone who edits the code, written for the options chosen |

`EmitCompilerGeneratedFiles` keeps the generated C# on disk. [From scratch](/guide/from-scratch)
gives its location.

## Options on every template

All three templates take these options:

| Option | Short name | Values | Default | Selects |
|---|---|---|---|---|
| `--test-framework` | `-tf` | `xunit`, `nunit` | `xunit` | xUnit v3 4.x with `DependencyModules.xUnit4`, or NUnit 4 with `DependencyModules.NUnit` |
| `--mocks` | `-mo` on `hardened-web`, `-m` on the other two | `nsubstitute`, `moq`, `fakeiteasy` | `nsubstitute` | The library behind `[Mock]`: the package `DependencyModules.NSubstitute`, `DependencyModules.Moq` or `DependencyModules.FakeItEasy`, and the assembly attribute `NSubstituteSupport`, `MoqSupport` or `FakeItEasySupport` |
| `--hardened-version` | `-hv` on `hardened-function` and `hardened-library`, none on `hardened-web` | A package version | The version of the template package | The value of `HardenedVersion`. See [Package versions](#package-versions) |
| `--skip-restore` | None | `true`, `false` | `false` | Skips the restore that `dotnet new` runs after writing the files |

## Refused combinations

The templates refuse five combinations:

| Command | `dotnet new` | The build | Regenerate with |
|---|---|---|---|
| `hardened-web --host aws-lambda --response-model union` | Exits with code 105 | Fails with `HTPL001` | `--response-model response` |
| `hardened-web --host azure-functions --response-model union` | Exits with code 105 | Fails with `HTPL007` | `--response-model response` |
| `hardened-web --contract smithy` with `--serializer message-pack-named` or `message-pack-keyed` | Exits with code 105 | Fails with `HTPL008` | `--contract openapi`, or `--serializer json` |
| `hardened-function --host gcp --trigger stream` | Succeeds | Fails with `HTPL005` | `--trigger queue` |
| `hardened-function --host azure --trigger invoke` | Succeeds | Fails with `HTPL006` | `--trigger queue`, or `hardened-web --host azure-functions` |

For the three `hardened-web` rows, `dotnet new` writes the files, prints the reason and exits with
code 105. It does not run the restore.

```console
$ dotnet new hardened-web -n Todos --host aws-lambda --response-model union
The template "Hardened Web Application" was created successfully.

Processing post-creation actions...
The post action 84c0da21-51c8-4541-9940-6ca19af04ee6 is not supported.
Description: --response-model union cannot be combined with this host: union needs net11.0, and neither the AWS Lambda managed runtime nor the Azure Functions worker runs a net11.0 assembly.
Manual instructions: Regenerate with --response-model response, which declares the same set on net8.0. The project written here does not build as it stands - the first build refuses with HTPL001.
```

The files stay on disk. Building them fails with the code in the table.

For the two `hardened-function` rows, `dotnet new` exits 0 without a warning. The first build fails
with the code in the table.

## Package versions

Every template writes `Directory.Packages.props` with a `HardenedVersion` property:

```xml
<HardenedVersion>0.0.0-HARDENED-VERSION</HardenedVersion>
```

Every Hardened package version in the file is `$(HardenedVersion)`. The value is the version of the
template package that wrote the project. `--hardened-version` writes another value into
`HardenedVersion`. No other file changes.

A scaffolded project keeps its `HardenedVersion` when a newer template package is installed. To
move a project to another release, change `HardenedVersion`.

`Directory.Packages.props` also sets `DependencyModulesVersion`. It versions the mock package in the
test project. Its value is the DependencyModules release that `Hardened.Shared.Testing` depends on.

## Next

| Page | Covers |
|---|---|
| [Getting started](/guide/getting-started) | The default `hardened-web` scaffold, run and tested |
| [From scratch](/guide/from-scratch) | An application built from packages, without a template |
| [Hosts](/guide/hosts) | What each `--host` value runs, and its `Program.cs` |
| [Writing a test](/guide/testing) | How the scaffolded tests run |
| [Triggers](/guide/triggers) | The trigger attributes, and the adapter each cloud uses |
