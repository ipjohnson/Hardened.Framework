# Google Cloud

A Hardened service runs on Google Cloud Run with `Hardened.Gcp.CloudRun.Runtime` and one adapter
package for each trigger its handlers use. Every source reaches the service as an HTTP request: a
Pub/Sub push, an Eventarc CloudEvent, a Cloud Scheduler job's request, or a caller's `POST` to an
operation.

With `Hardened.Gcp.CloudRun.PubSub` referenced, `[Queue("orders")]` receives the messages of the
Pub/Sub push subscription named `orders`. The template writes this handler in
`src/Orders/OrderHandler.cs`:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

The template's application class, in `src/Orders/Application.cs`, is a `partial` class marked
`[HardenedModule]` and `[CloudRunRuntime]`:

```csharp
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[CloudRunRuntime]
public partial class Application;
```

A push subscription named `orders` sends this request. In this exchange the service runs locally:

```http
POST /
Content-Type: application/json

{
  "message": {
    "data": "eyJpZCI6IkEtMSIsInF1YW50aXR5IjoyfQ==",
    "messageId": "2070443601311540",
    "publishTime": "2026-09-23T19:13:55.749Z"
  },
  "subscription": "projects/my-project/subscriptions/orders"
}

HTTP/1.1 200 OK
```

The adapter reads the request, and the handler receives the message inside it. The message's `data`
is base64. `eyJpZCI6IkEtMSIsInF1YW50aXR5IjoyfQ==` is `{"id":"A-1","quantity":2}`, which the
handler receives as its `Order`. The service answers 200 with no body when the handler returns.
Pub/Sub reads a 200 as an acknowledgement. [Queues](/gcp/queue) covers the push body and the
headers the handler receives.

`dotnet new hardened-function --host gcp` writes a Cloud Run service with tests and a `Dockerfile`.
The examples come from `dotnet new hardened-function -n Orders --host gcp --trigger queue`.
`--trigger` picks the trigger, and `invoke` is the default.
[Project templates](/guide/project-templates) covers the options. [Triggers](/guide/triggers)
covers the trigger attributes and what they bind on every cloud.

The same application also runs as a Cloud Functions 2nd gen function. A web application on Cloud
Run is the `hardened-web` template with `--host cloud-run`. [Web services](/gcp/web) covers both.

## Packages and modules

| Package | Serves | Module attribute | Build property |
|---|---|---|---|
| `Hardened.Gcp.CloudRun.Runtime` | The Cloud Run host: `[CloudRunRuntime]`, `CloudRunHost` and `TriggerFrontDoor`. Every Google Cloud project references it | `[CloudRunRuntime]` | `HardenedHttpModule` |
| `Hardened.Gcp.CloudRun.PubSub` | `[Queue]`, from a Pub/Sub push subscription, and `[Topic]`, from an Eventarc trigger on a Pub/Sub topic | `[PubSubModule]` | `HardenedQueueModule` and `HardenedTopicModule` |
| `Hardened.Gcp.CloudRun.Scheduler` | `[Timer]`, from a Cloud Scheduler job | `[SchedulerModule]` | `HardenedTimerModule` |
| `Hardened.Gcp.CloudRun.Invoke` | `[HardenedFunction]`, invoked by a POST to the service | `[InvokeModule]` | `HardenedInvokeModule` |
| `Hardened.Gcp.CloudRun.Storage` | `[Blob]`, from Cloud Storage, through Eventarc or a Pub/Sub notification | `[StorageModule]` | `HardenedBlobModule` |
| `Hardened.Gcp.CloudRun.Firestore` | `[Change]`, from Firestore, through Eventarc | `[FirestoreModule]` | `HardenedChangeModule` |
| `Hardened.Gcp.CloudRun.Eventarc` | `[Event]`, any CloudEvent Eventarc delivers | `[EventarcModule]` | `HardenedEventModule` |
| `Hardened.Gcp.CloudRun` | The Cloud Run host and every adapter above, in one reference | Every attribute above | Every property above |
| `Hardened.Gcp.Functions.Runtime` | The Cloud Functions 2nd gen host: `HardenedFunctionsStartup<TApplication>` and `CloudFunctionHost` | None | None |
| `Hardened.Gcp.Functions.SourceGenerator` | The generator that writes a function's entry type | None | None |
| `Hardened.Gcp.CloudRun.Testing` | `[CloudRunTesting]`, in a test project | None | None |

An application does not write an adapter's module attribute to get the adapter. The build registers
the module of each trigger the handlers use, from the build property, in the generated
`Application.TriggerModules.cs`. [Triggers](/guide/triggers) covers the mechanism.

An application writes a module attribute to change a setting. `[SchedulerModule]` and
`[InvokeModule]` each have a `Prefix` setting, which [Timers](/gcp/timer) and
[Invocations](/gcp/invoke) cover. The other modules have no settings. Each module attribute is in
the namespace named like its package, such as `Hardened.Gcp.CloudRun.PubSub`.

A service whose handlers are all trigger handlers also runs without `[CloudRunRuntime]`, because
each adapter's module brings it. A web application whose routes are in a library names
`[CloudRunRuntime]` on its application class. [Web services](/gcp/web) covers it.

Google Cloud has no adapter for `[Stream]`. Every other trigger has an adapter. A project with a
`[Stream]` handler fails to build with `HRDF001`:

```text
error HRDF001: Handlers in this project use [Stream], but no referenced runtime declares a module for it. Reference a runtime package that supports Stream triggers, or set <HardenedStreamModule> to the module that should serve them.
```

The template refuses `--host gcp --trigger stream`. [Project templates](/guide/project-templates)
covers the refusal.

`Hardened.Gcp.CloudRun` puts every adapter in the deployment, whatever the handlers use. The build
notes each unused adapter as `HRDF003`. [Triggers](/guide/triggers) covers `HRDF003`.

Of the adapters, only `Hardened.Gcp.CloudRun.Firestore` brings Google packages:
`Google.Events.Protobuf` and the packages it depends on. The publish output of the queue service at
the top of this page holds no `Google.*` assembly.

The queue service references these packages, written here without central package management:

```xml
<ItemGroup>
  <PackageReference Include="Hardened.Shared.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Requests.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Gcp.CloudRun.PubSub" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.1" />
  <PackageReference Include="Hardened.Library.SourceGenerator" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Function.SourceGenerator" Version="0.0.0-HARDENED-VERSION" PrivateAssets="all" />
</ItemGroup>
```

The service references `Microsoft.Extensions.Logging.Console` for the console logger that
`Program.cs` installs. The template pins it at 8.0.1.

## Handlers in one service

One service can serve trigger handlers, `[HardenedFunction]` operations and web routes together, on
one port.

`TriggerFrontDoor` runs before routing. It asks each adapter whether the request is that adapter's
delivery. The first adapter that reads the request turns it into the trigger's route, such as
`QUEUE /orders`, and the request goes to that handler. A request that no adapter reads goes to the
web routes.

The adapters for a specific source read a delivery before the two general ones: the Pub/Sub push,
which serves `[Queue]`, and a CloudEvent of any type, which serves `[Event]`. A Cloud Storage event
reaches `[Blob]` when `Hardened.Gcp.CloudRun.Storage` is referenced, and an `[Event]` handler for
its source and type when it is not. [Events](/gcp/event) covers `[Event]` routes.

::: warning
A request reaches a trigger handler because of its shape, not because of who sent it. A body shaped
like a Pub/Sub push reaches the `[Queue]` handler whether Pub/Sub sent it or not. A service with
trigger handlers must keep Cloud Run's invoker check, with `--no-allow-unauthenticated`, as
[Deploying](#deploying) shows. Deployed with `--allow-unauthenticated`, it lets anyone call its
trigger handlers. Serve public web routes from a service of their own.
:::

| Case | What happens |
|---|---|
| Any mix of trigger handlers, `[HardenedFunction]` operations and web routes | Each request reaches its handler |
| A delivery whose source no handler names | The request answers 500, and the log says `No handler is registered for QUEUE /no-such-queue. An event source is wired to this function that no trigger attribute declared.` |
| A `[HardenedFunction]` without a name, or a handler whose route name equals its method's name, such as `[Queue("Audit")]` on a method named `Audit` | That handler receives every delivery that no other handler's route matches, and the request answers 200, so the source counts it as delivered. [Triggers](/guide/triggers) covers the rule, and [Invocations](/gcp/invoke) covers it on Google Cloud |

## The entry point

A Cloud Run service's entry point is `Program.cs`, which the template writes. Nothing generates it.

```csharp
using Hardened.Gcp.CloudRun.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orders;

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole(options => options.SingleLine = true));

services.AddHardenedEnvironment(new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(services, CloudRunHost.Listen);

await app.StartAsync();

Console.WriteLine($"Listening on {string.Join(", ", app.Addresses)}");

await CloudRunHost.RunAsync(app);
```

`CloudRunHost` is in `Hardened.Gcp.CloudRun.Runtime.Hosting`. Its `Listen` method listens on every
network interface, on the port that `PORT` names, or on 8080 when `PORT` is unset. Its `RunAsync`
method lets requests in flight finish when the service is stopped. [Hosts](/guide/hosts) covers
`RunAsync` and `HardenedKestrelApplication`.
[Environments](/guide/environments) covers `AddHardenedEnvironment`.

The service starts without creating any handler. When a handler needs a service that nothing
registers, the start succeeds. The first request that reaches that handler answers 500, and the log
names the missing service.

A Cloud Functions 2nd gen function has no `Program.cs`. With three more packages, the build writes
its entry point. [Hosts](/guide/hosts) shows the packages, and [Web services](/gcp/web) covers the
function.

## Running locally

`dotnet run --project src/Orders` starts the service on the port in `PORT`, or on 8080:

```console
$ dotnet run --project src/Orders
Listening on http://[::]:8080
```

No emulator starts. The service is the process, and a delivery is an HTTP request posted to it.
`curl` posts the request a source sends, such as the push at the top of this page, saved as
`push.json`:

```bash
curl -i -X POST localhost:8080/ -H 'Content-Type: application/json' --data-binary @push.json
```

Each trigger page shows the request its source sends: [Queues](/gcp/queue), [Topics](/gcp/topic),
[Timers](/gcp/timer), [Events](/gcp/event), [Changes](/gcp/change), [Blobs](/gcp/blob) and
[Invocations](/gcp/invoke).

The request log names the HTTP request, such as `POST /`. When a handler throws, the failure line
names the trigger's route, such as `QUEUE /orders request failed`. A push whose handler throws
`InvalidOperationException("refused queue")` logs these lines, with the stack trace cut after the
exception's message:

```text
info: Hardened.Requests.Runtime.Logging.RequestLogger[78000] POST / started
fail: Hardened.Requests.Runtime.Logging.RequestLogger[0] QUEUE /orders request failed System.InvalidOperationException: refused queue
info: Hardened.Requests.Runtime.Logging.RequestLogger[78002] POST /  finished status code '500'  duration 00:00:00.0093195
```

The template's tests deliver to the handlers with no Google Cloud project and no running service.
[Testing functions](/guide/testing-functions) and [Testing](/gcp/testing) cover them.
[Hosts](/guide/hosts) covers running a Cloud Functions project locally.

## When a handler throws

On every trigger, a handler that throws answers the request with 500 and the body
`{"type":"ServerError","message":"The server could not complete this request.","details":""}`. A web route
does the same. A handler that returns answers 200. A trigger's 200 has no body, and a
`[HardenedFunction]`'s 200 carries the handler's return value.

Each request carries one message, event or run. No Google Cloud adapter delivers a batch, so no
module has a setting that reports failed items. [Triggers](/guide/triggers) covers batches.

| Trigger | When a handler throws | What happens next | Page |
|---|---|---|---|
| `[Get]` and the other verbs | The request answers 500 | The caller receives the 500 | [Web services](/gcp/web) |
| `[HardenedFunction]` | The request answers 500 | The caller receives the 500 | [Invocations](/gcp/invoke) |
| `[Queue]` | The push answers 500 | Pub/Sub sends the message again, following the subscription's retry policy, until it is acknowledged, it expires, or it moves to the subscription's dead-letter topic | [Queues](/gcp/queue) |
| `[Topic]` | The CloudEvent answers 500 | Eventarc sends the event again, following the retry policy of the subscription it created | [Topics](/gcp/topic) |
| `[Timer]` | The job's request answers 500 | Cloud Scheduler records a failed run. By default it does not retry, and the job runs again at its next scheduled time | [Timers](/gcp/timer) |
| `[Event]` | The CloudEvent answers 500 | Eventarc sends the event again, following the retry policy of the subscription it created | [Events](/gcp/event) |
| `[Change]` | The CloudEvent answers 500 | Eventarc sends the event again, following the retry policy of the subscription it created | [Changes](/gcp/change) |
| `[Blob]` | The CloudEvent or the notification's push answers 500 | Eventarc, or the notification's push subscription, sends it again | [Blobs](/gcp/blob) |
| `[Stream]` | No adapter. The build fails with `HRDF001` | Not applicable | [Triggers](/guide/triggers) |

Each trigger's page covers its source's retries, dead-lettering and retention.

## Deploying

Hardened has no deployment package for Google Cloud. The Google Cloud packages are the ones in
[Packages and modules](#packages-and-modules).

The template writes a `Dockerfile`. It publishes the project with the .NET 8 SDK image and runs the
output on the ASP.NET Core 8 runtime image:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Orders/Orders.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Orders.dll"]
```

`docker build -t orders .` in the solution directory builds the image.
`gcloud run deploy orders --source . --region us-central1 --no-allow-unauthenticated` uploads the
source, and Cloud Build builds it with the `Dockerfile`
([Deploy services from source code](https://docs.cloud.google.com/run/docs/deploying-source-code)).
The image is stored in an Artifact Registry repository named `cloud-run-source-deploy`.

```bash
docker build -t orders .
docker run --rm -p 8080:8080 orders
gcloud run deploy orders --source . --region us-central1 --no-allow-unauthenticated
```

`--no-allow-unauthenticated` keeps Cloud Run's invoker check, so a caller needs the Cloud Run
Invoker role
([Allowing public (unauthenticated) access](https://docs.cloud.google.com/run/docs/authenticating/public)).
A push subscription that uses authentication sends a JWT, signed for its service account, in the
request's `Authorization` header ([Push subscriptions](https://docs.cloud.google.com/pubsub/docs/push)).
Each trigger page covers wiring its source to the service, and the account it calls with:
[Queues](/gcp/queue), [Topics](/gcp/topic), [Timers](/gcp/timer), [Events](/gcp/event),
[Changes](/gcp/change), [Blobs](/gcp/blob) and [Invocations](/gcp/invoke).

[Environments](/guide/environments) covers naming the environment of a deployed service.
[Web services](/gcp/web) covers deploying a Cloud Functions 2nd gen function.

## The container runtime contract

Cloud Run sends requests to the port in `PORT`, 8080 by default, and the service has to listen on
every interface ([Container runtime contract](https://docs.cloud.google.com/run/docs/container-contract)).
`CloudRunHost.Listen` does both. Cloud Run sends `SIGTERM` before it stops an instance, and
`SIGKILL` 10 seconds later. [Hosts](/guide/hosts) covers how `CloudRunHost.RunAsync` uses those 10
seconds.

Cloud Run sets `K_SERVICE`, `K_REVISION` and `K_CONFIGURATION` in the container. A trigger handler
reads them with `Transport.Get(key)` on its request, under these keys:

| Key | From | Holds |
|---|---|---|
| `faas.name` | `K_SERVICE` | The service's name |
| `faas.version` | `K_REVISION` | The revision serving the request |
| `gcp.cloud_run.configuration` | `K_CONFIGURATION` | The configuration that created the revision |

The request is an `IExecutionRequest`, from `Hardened.Requests.Abstract.Execution`, which a handler
takes as a parameter. A web request's `Transport` does not carry these keys.

## Next

- [Triggers](/guide/triggers): the trigger attributes, and what `HRDF001` and `HRDF003` mean.
- [Queues](/gcp/queue): Pub/Sub push subscriptions and the headers a queue handler receives.
- [Web services](/gcp/web): web routes on Cloud Run, and running as a Cloud Functions 2nd gen
  function.
- [Testing](/gcp/testing): testing on Google Cloud.
