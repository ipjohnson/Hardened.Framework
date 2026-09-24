# Web services

`[CloudRunRuntime]` serves an application's routes as a Google Cloud Run service. The same
application also runs as a Cloud Functions 2nd gen function.

`dotnet new hardened-web -n Todos --host cloud-run` writes such an application: a library with the
routes, a host project for Cloud Run, a client project, a test project and a `Dockerfile`. The host
project holds the application class, `src/Todos.Host/Application.cs`. This one comes from the same
command with `--openapi-ui false`, which leaves out the `[HardenedOpenApiUi]` attribute and its `using`
line:

```csharp
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Todos.Host;

[HardenedModule]
[CloudRunRuntime]
[TodosLibrary]
public partial class Application;
```

After `dotnet run --project src/Todos.Host`, the service answers the library's routes:

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

`[CloudRunRuntime]` brings `[KestrelRuntime]`, so the service runs on the Kestrel host.
[Hosts](/guide/hosts) covers the Kestrel host and the service's `Program.cs`. The routes, filters
and parameter binding are the ones the application has on every host. [Routing](/guide/routing)
covers them.

## Packages and the application class

The host project references `Hardened.Gcp.CloudRun.Runtime` and `Hardened.Web.Kestrel.Runtime`:

```xml
<PackageReference Include="Hardened.Web.Kestrel.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.0.0-HARDENED-VERSION" />
```

The Google Cloud [Overview](/gcp/) lists the other Google Cloud packages.

`Hardened.Gcp.CloudRun.Runtime` sets the build property `HardenedHttpModule` to
`Hardened.Gcp.CloudRun.Runtime.CloudRunRuntime`. The build registers `CloudRunRuntime` from that
property when a project compiles its routes together with its application class:

| Where the routes are | Attributes on the application class |
|---|---|
| In the same project as the application class | `[HardenedModule]` only |
| In a referenced library, as in the template | `[HardenedModule]` and `[CloudRunRuntime]` |

When the routes are in a library and the application class leaves out `[CloudRunRuntime]`, the
host project still builds. The service stops at startup:

```text
Unhandled exception. System.InvalidOperationException: No service for type 'Microsoft.AspNetCore.Hosting.Server.IHttpApplication`1[Hardened.Web.Kestrel.Runtime.Impl.HardenedHttpApplication+RequestContext]' has been registered.
```

The attribute has no settings.

## The container image

The Google Cloud [Overview](/gcp/) shows the `hardened-function` template's `Dockerfile`. The web
template's `Dockerfile` has the same lines, except that it publishes
`src/Todos.Host/Todos.Host.csproj` and starts `Todos.Host.dll`.

In the image, the service listens on `PORT`, or on 5080 when `PORT` is unset. The `Dockerfile`
names port 8080 with `EXPOSE`. Nothing listens on 8080 without `PORT`. Cloud Run
[sets `PORT`](https://docs.cloud.google.com/run/docs/container-contract) in the service's
container, so a deployed service listens where Cloud Run sends requests. Locally, the `docker run`
command sets `PORT`. Both commands run in the solution directory:

```bash
docker build -t todos .
docker run --rm -p 8080:8080 -e PORT=8080 todos
```

## Trigger handlers in a web service

A web service can also serve trigger handlers, on the same port as its routes. The host project
holds the handler. It references the trigger's adapter package and
`Hardened.Function.SourceGenerator`.

The web template's `Directory.Packages.props` pins neither package. Each needs a `PackageVersion`
line. Without one, the restore fails with `NU1010`. For the Pub/Sub adapter package,
`Hardened.Gcp.CloudRun.PubSub`, these lines go in `Directory.Packages.props`:

```xml
<PackageVersion Include="Hardened.Gcp.CloudRun.PubSub" Version="$(HardenedVersion)" />
<PackageVersion Include="Hardened.Function.SourceGenerator" Version="$(HardenedVersion)" />
```

These lines go in `src/Todos.Host/Todos.Host.csproj`:

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.PubSub" />
<PackageReference Include="Hardened.Function.SourceGenerator" PrivateAssets="all" />
```

Without an adapter package, the service reads no request as a trigger. A Pub/Sub push posted to
the template's service answers 404.

The Google Cloud [Overview](/gcp/) covers which handler receives a request, and what a failed
delivery does. [Triggers](/guide/triggers) covers trigger handlers in a library project.

## Running on Cloud Functions

`Hardened.Gcp.Functions.Runtime` runs the same `[CloudRunRuntime]` application as a Cloud Functions
2nd gen function. Google's documentation calls these functions
[Cloud Run functions](https://docs.cloud.google.com/run/docs/deploy-functions). The application
class, the library and the tests do not change. The host project adds three packages and deletes
`Program.cs`. [Hosts](/guide/hosts) shows the packages and covers running the function locally.

The build writes the entry type below into the host project, as `Application.CloudFunction.cs`. The
entry type's name is the application's name with `CloudFunction` appended, in the application's
namespace:

```csharp
namespace Todos.Host
{
    [global::Google.Cloud.Functions.Hosting.FunctionsStartup(typeof(global::Hardened.Gcp.Functions.Runtime.Hosting.HardenedFunctionsStartup<global::Todos.Host.Application>))]
    public sealed class ApplicationCloudFunction : global::Google.Cloud.Functions.Framework.IHttpFunction
    {
        private readonly global::Hardened.Gcp.Functions.Runtime.Hosting.CloudFunctionHost _host;

        public ApplicationCloudFunction(global::Hardened.Gcp.Functions.Runtime.Hosting.CloudFunctionHost host)
        {
            _host = host;
        }

        public global::System.Threading.Tasks.Task HandleAsync(global::Microsoft.AspNetCore.Http.HttpContext context)
        {
            return _host.HandleAsync(context);
        }
    }
}
```

The Functions Framework creates the entry type for each request. The entry type passes the request
to `CloudFunctionHost`, which runs it through the same filters, routes and handlers as the Cloud
Run service.

`HardenedFunctionsStartup<Application>` adds the application's services to the Functions
Framework's service collection. It also registers the Hardened environment from the process's
arguments. [Environments](/guide/environments) covers the environment.
`HardenedFunctionsStartup<TApplication>` and `CloudFunctionHost` are in
`Hardened.Gcp.Functions.Runtime.Hosting`.

Trigger handlers serve on a function as they do on Cloud Run. A function has no `Program.cs` to
add a logger. The application's log lines go through the Functions Framework's console logger.

### Two applications in one assembly

A second `[HardenedModule]` class in the host project fails the build with `HRDR004`. The same
build warns `HRDGF001`:

```text
error HRDR004: 'AdminApplication' and 'Application' are both Hardened entry points in this assembly. Each gets its own routing table over every handler here, so the two describe identical routes and nothing says which one a host runs. To share routes across applications, move the handlers into a [WebLibrary] project and reference it. To keep both entry points deliberately, set <NoWarn>$(NoWarn);HRDR004</NoWarn>.
warning HRDGF001: This assembly holds 2 applications, so it has 2 Cloud Functions entry types and the deployment has to name one. Set --entry-point, or FUNCTION_TARGET, to one of: Todos.Host.AdminApplicationCloudFunction, Todos.Host.ApplicationCloudFunction.
```

With `HRDR004` suppressed, the build succeeds. It writes one entry type for each application. The
function then stops at startup unless the target is named:

```text
Unhandled exception. System.ArgumentException: Multiple Cloud Function types found. Please specify the function to run via the command line or the FUNCTION_TARGET environment variable.
```

`FUNCTION_TARGET` names the target locally. `--entry-point` names it in a deployment. The
[Diagnostics](/reference/diagnostics) reference lists both codes.

## Failed requests

Cloud Run and Cloud Functions answer a failed request with these statuses:

| Request | Cloud Run | Cloud Functions |
|---|---|---|
| The handler throws | 500 with the error body | 500 with the error body |
| No route matches the path | 404 with no body | 404 with no body |
| A validation constraint fails | 400 with the error body | 400 with the error body |

On Cloud Functions, `CloudFunctionHost` answers a handler's exception itself. The Functions
Framework's error handling does not see the exception. Hardened's request log records the failure
once. The Google Cloud [Overview](/gcp/) covers what a trigger's source does after a 500.

## Deploying

The Google Cloud [Overview](/gcp/) covers building the image and `gcloud run deploy --source`. A
public web service deploys with `--allow-unauthenticated`:

```bash
gcloud run deploy todos --source . --region us-central1 --allow-unauthenticated
```

The flag gives the
[Cloud Run Invoker role](https://docs.cloud.google.com/run/docs/authenticating/public) to
`allUsers`. Without the flag, Cloud Run's invoker check stays on. A caller then needs the role.

A Cloud Functions 2nd gen function deploys with
[`gcloud functions deploy`](https://docs.cloud.google.com/sdk/gcloud/reference/functions/deploy).
The template's solution holds several projects. The deployment runs in the solution directory:

```bash
gcloud functions deploy todos --gen2 --runtime dotnet8 --region us-central1 \
    --source . --entry-point Todos.Host.ApplicationCloudFunction --trigger-http \
    --allow-unauthenticated --set-build-env-vars GOOGLE_BUILDABLE=src/Todos.Host
```

| Flag | What it does |
|---|---|
| `--runtime dotnet8` | Selects .NET 8, which Google [lists](https://docs.cloud.google.com/functions/docs/runtime-support) for Cloud Run functions as `dotnet8` |
| `--entry-point` | Names the entry type by its full name |
| `--trigger-http` | Makes the function an HTTP function |
| `--set-build-env-vars` | Names the host project as a path, in the build variable [`GOOGLE_BUILDABLE`](https://docs.cloud.google.com/docs/buildpacks/service-specific-configs) |

A [`.gcloudignore`](https://github.com/GoogleCloudPlatform/functions-framework-dotnet/blob/main/docs/deployment.md)
file with these lines, in the solution directory, keeps local build output out of the upload:

```text
bin/
obj/
```

The template writes no `.gcloudignore` file.

[Environments](/guide/environments) covers naming the environment in a deployed service or function.

## Limits

A service that references `Hardened.Gcp.CloudRun.PubSub` or `Hardened.Gcp.CloudRun.Storage`
answers 413 to a JSON `POST` whose body is over 16 MiB and sent without a `Content-Length`,
whatever its path. The route never runs.

A test project that references a Cloud Functions project, such as the `hardened-function`
template's test project, reports `HRDGF002`:

```text
warning HRDGF002: This project references Hardened.Gcp.Functions.Runtime but not Google.Cloud.Functions.Hosting, so the Functions Framework entry point is not generated and the build has no Main. Add <PackageReference Include="Google.Cloud.Functions.Hosting" />, or set AutoGenerateEntryPoint to false if this project supplies its own.
```

The build succeeds.

## Next

| Page | Covers |
|---|---|
| [Overview](/gcp/) | The Google Cloud packages, the `Dockerfile`, and deploying to Cloud Run |
| [Hosts](/guide/hosts) | The service's `Program.cs`, and the Cloud Functions packages |
| [Testing](/gcp/testing) | Testing a web service on Cloud Run and a function on Cloud Functions |
| [Streaming responses](/guide/streaming) | Streaming a response, and which hosts stream |
| [Invocations](/gcp/invoke) | Operations that a caller invokes with a `POST` |
