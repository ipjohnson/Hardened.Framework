# Cloud Run application types

Every Cloud Run application is one entry point class carrying `[HardenedModule]` and
`[CloudRunRuntime]`. The host attribute is the whole of what the application says about where it
runs. Which sources it serves is said by the trigger attributes on its handlers, and each trigger
is served by one adapter package that binds a build property the generator reads.

| Source | Handler carries | Package | Route |
|---|---|---|---|
| HTTP | `[Get]`, `[Post]`, `[Put]`, `[Patch]`, `[Delete]` | `Hardened.Gcp.CloudRun.Runtime` | The route the attribute names |
| A push subscription | `[Queue]` | `Hardened.Gcp.CloudRun.PubSub` | `QUEUE /{subscription}` |
| An Eventarc trigger on a topic | `[Topic]` | `Hardened.Gcp.CloudRun.PubSub` | `TOPIC /{topic}` |
| A Cloud Scheduler job | `[Timer]` | `Hardened.Gcp.CloudRun.Scheduler` | `TIMER /{name}` |
| A caller | `[HardenedFunction]` | `Hardened.Gcp.CloudRun.Invoke` | `INVOKE /{operation}` |
| Cloud Storage | `[Blob]` | `Hardened.Gcp.CloudRun.Storage` | `BLOB /{bucket}` |
| Firestore | `[Change]` | `Hardened.Gcp.CloudRun.Firestore` | `CHANGE /{collection}` |
| Any CloudEvent | `[Event]` | `Hardened.Gcp.CloudRun.Eventarc` | `EVENT /{source}/{type}` |

`[Stream]` has no row. Google has no sharded, checkpointed stream to bind it to, and a project
that writes it fails with `HRDF001` naming the gap. That is decision D4 of the cloud lines plan.

Unlike Lambda, where the invocation loop is the host and the adapter recognises the payload, every
one of these arrives as an HTTP request to one Kestrel socket. The host's job is to tell them apart
ahead of routing. The transport modules of the Amz line, `[LambdaWebModule]` and
`[LambdaFunctionModule]`, have no counterpart here: there is one host, and it serves both paths.

## Five rules

1. **`[HardenedModule]` is always required.** It is what marks a class as an entry point.
2. **`[CloudRunRuntime]` goes on the application class itself.** A Cloud Run service is a
   container listening on a port, so the host is a fact about the deployment and is named the way
   `[KestrelRuntime]` is. It is the one host attribute in the line; the adapters are never named.
3. **A trigger attribute is what pulls an adapter in.** `[Queue]` on a handler makes the generator
   read `HardenedQueueModule` from the referenced package's build properties and register
   `PubSubModule`. An adapter is written out on the application only to set a property, and only
   `SchedulerModule` and `InvokeModule` have one, `Prefix`.
4. **Reference the source generators as analyzers.** `Hardened.Library.SourceGenerator` always,
   `Hardened.Function.SourceGenerator` for a project with trigger handlers, and
   `Hardened.Web.SourceGenerator` for one with routes. Without them nothing is generated and
   `PopulateServiceCollection` does not compile.
5. **`Program.cs` is written, not generated.** It is the Cloud Run container contract, and it is
   the same file for every application type.

## The entry point

```csharp
using Hardened.Gcp.CloudRun.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderIntake;

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole(options => options.SingleLine = true));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(services, CloudRunHost.Listen);

await app.StartAsync();

Console.WriteLine($"Listening on {string.Join(", ", app.Addresses)}");

await CloudRunHost.RunAsync(app);
```

`CloudRunHost.Listen` is `KestrelListen.FromEnvironment` with 8080 as the default: every
interface on `PORT`. `CloudRunHost.RunAsync` is the Kestrel application's `RunAsync` overload
that registers `SIGTERM` and `SIGINT` with `PosixSignalRegistration`, cancels the runtime's own
handling, and stops the server within ten seconds, Cloud Run's grace. The plain `RunAsync` returns
on `ProcessExit` and the process exits before the server has drained; the container tier's
shutdown test saw a response cut off that way, which is why the helper exists.

The project is an `Exe`. The container tier runs the build output as `dotnet <assembly>.dll` in
`mcr.microsoft.com/dotnet/aspnet:8.0`, and the templates' Dockerfile publishes it into the same
image.

## The front door

`TriggerFrontDoor` is a middleware filter the host installs ahead of routing when any
`ITriggerEnvelope` is registered. Every adapter registers one. On each request the front door:

1. Passes the request through untouched when nothing is registered, when the request is already a
   trigger request, or when its declared length is over 16 MiB.
2. Buffers the body, answering 413 when an undeclared body exceeds the limit.
3. Asks every envelope, in order, whether it recognises the request. The typed envelopes go first,
   Storage, Firestore, the Pub/Sub topic form, Scheduler and Invoke, and the two fallbacks last:
   the plain push, which claims any JSON POST that is not a CloudEvent, and Eventarc, which claims
   any CloudEvent nothing more specific took.
4. Forks the chain with a `CloudRunTriggerRequest` carrying the trigger scheme and route when one
   unwraps it, or continues with the buffered body when none does.

A trigger request is dispatched by the function path and a web request by the web path. Both are
one `IHandlerDispatch`: the host decorates the container after every module has registered, so
`FunctionDispatchFilter` and the web dispatch are found and composed into `CloudRunDispatch`,
registered as both `IHandlerDispatch` and `IWebExecutionHandlerService`. That is what lets a
`[Get]` and a `[Queue]` live in one project, and what lets the neutral test delivery, which
refuses two dispatches, run against a Cloud Run application.

`CloudRunTransportInfo` wraps the request's transport info and adds `faas.name`, `faas.version`
and `gcp.cloud_run.configuration` from `K_SERVICE`, `K_REVISION` and `K_CONFIGURATION`.

## The two Pub/Sub deliveries

`[Queue]` and `[Topic]` are both Pub/Sub and both live in the PubSub package, and they route on
different things because the two deliveries carry different names. A push body carries
`subscription` and never the topic; an Eventarc delivery of `messagePublished` carries the topic
in `ce-source`. So a queue is a push subscription routed on its name, and a topic is an Eventarc
trigger routed on the topic's. Decision D5 of the plan. A subscription named after a topic could
have stood in, at the cost of a rule the deployment has to know, and was not adopted.

Three envelopes serve the two:

| Envelope | Recognises | Routes as |
|---|---|---|
| `PubSubUnwrappedPushEnvelope` | `x-goog-pubsub-subscription-name` on the request | `QUEUE /{subscription}` |
| `PubSubTopicEnvelope` | A CloudEvent typed `google.cloud.pubsub.topic.v1.messagePublished` | `TOPIC /{ce-source's last segment}` |
| `PubSubPushEnvelope` | A JSON POST with `message` and `subscription`, and not a CloudEvent | `QUEUE /{subscription's last segment}` |

The push reader, `PubSubPushBody`, lives in the Runtime package rather than here, because the
Storage adapter reads the same body for a bucket notification without depending on the PubSub
package, which would bind `HardenedQueueModule` into a service that has no queue.

## Firestore

The one adapter with a Google dependency. A Firestore event is `application/protobuf`, a
`DocumentEventData`, and `Google.Events.Protobuf` decodes it; keeping that in its own package is
what keeps protobuf out of every other service. The envelope routes on the collection, the
second-to-last segment of the document path in `ce-subject`, writes the document after the change
as plain JSON for the body, `FirestoreValueJson`, and keeps the whole event on the request as a
`FirestoreChange`, a `CloudRunTriggerRequest` subclass. `[OldValue]` is an `ICustomBindingAttribute`
that reads the old document off that request and, for a handler's own type, deserializes the JSON
projection of it through the pipeline's `IContextSerializationService` on a cloned context, which
is AOT-clean. On a delete the body is the old document, because there is no new one.

Native AOT is proven for this package the only way it can be. The trim analyzers pass on the
package's own code, which says nothing about `Google.Protobuf` under ILC, so the AOT SUT
references this adapter beside the Pub/Sub one and the CI probe posts a protobuf document event
to the native binary. It published with zero IL warnings and decoded the event, old value
included.

## Storage

Two production forms, one projection. An Eventarc CloudEvent typed
`google.cloud.storage.object.v1.*` carries the object's metadata as its data, and a Pub/Sub
notification carries the same metadata as the message data with `eventType`, `bucketId`,
`objectId`, `objectGeneration` and `eventTime` as attributes. `StorageNotification` is the record
both are read into, with the notification's event vocabulary, and its JSON is the body. The
notification form is the one the Pub/Sub emulator can deliver, which is why the container tier
uses it; fake-gcs-server publishing the notification itself was left optional, and its
notification path is reported unreliable.

## Scheduler and Invoke

Neither has an envelope to recognise, so the route travels in the URL. `[Timer("nightly")]` is
`POST /_triggers/timer/nightly`; `[HardenedFunction("process")]` is `POST /_triggers/invoke/process`.
The prefixes are the two modules' `Prefix` property, the one reason either is written out.
Scheduler's `X-CloudScheduler-JobName` is cross-checked against the URL when present, and a
mismatch is an `InvalidOperationException`, answered 500, rather than a run under the wrong name.
Invoke writes the handler's return value as the response body, which is what makes it the one
trigger that answers.

## Project shape

An application project references the host, the adapters for the triggers it serves, and the
generators as analyzers:

```xml
<PropertyGroup>
    <OutputType>Exe</OutputType>
</PropertyGroup>

<ItemGroup>
    <PackageReference Include="Hardened.Shared.Runtime" />
    <PackageReference Include="Hardened.Requests.Runtime" />
    <PackageReference Include="Hardened.Functions.Runtime" />
    <PackageReference Include="Hardened.Gcp.CloudRun.Runtime" />
    <PackageReference Include="Hardened.Gcp.CloudRun.PubSub" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" />
    <PackageReference Include="Hardened.Library.SourceGenerator" />
    <PackageReference Include="Hardened.Function.SourceGenerator" PrivateAssets="all" />
</ItemGroup>
```

Inside this repository the packages are project references, and a project reference carries no
build assets, so a fixture imports the `.targets` files from the source tree:
`Hardened.Functions.Runtime.targets` declares the trigger properties, each adapter's binds one,
and `Hardened.Gcp.CloudRun.Runtime.targets` binds `HardenedHttpModule` to the host so a `[Get]`
beside a `[Queue]` does not trip `HRDF001`. A consumer gets the same files from `buildTransitive/`.

## Running locally

`dotnet run` starts the service on 8080, or on `PORT`. There is no emulator to start and nothing
to install: a trigger is an HTTP request, and any of them can be posted by hand. The Pub/Sub
emulator is only in the container tier, where `Testcontainers.PubSub` starts it and Google's
client publishes to it.

## Testing

Four rungs, all on the same test methods. The pipeline rung is `[assembly: FunctionTesting]` and
names no cloud. The envelope rung is `[assembly: CloudRunTesting]` beside `[assembly: WebTesting]`:
`CloudRunEnvelopeDelivery` builds the exact wire shape for the scheme, a push, a CloudEvent, a
Scheduler request, and posts it through `Hardened.Web.Testing`'s host seam. The socket rung is
`[KestrelRuntime]` on a class under `[assembly: KestrelTesting]`, which is the same delivery over
loopback. The container rung is a project per family in `Hardened.Simulators.slnx`, mounting the
fixture's build output into `aspnet:8.0` and driving it through the emulator or a hand-built
request, observed through `HARDENED-OBSERVED` lines the fixture prints.

`[Event]` has no façade, because its route has two segments; its tests send through
`ITriggerDelivery` directly. A class can opt back to the pipeline delivery with
`[PipelineDelivery]` from `Hardened.Functions.Testing`.

## Building

```bash
dotnet build filters/gcp.slnf
dotnet test  filters/gcp.slnf
dotnet build filters/gcp.slnf --configuration Release -p:ContinuousIntegrationBuild=true
```

The third is the gate CI applies; it turns warnings into errors, and the trim and AOT analyzers
are on in every runtime package through `IsAotCompatible`. The container tier is
`dotnet test` on a project in `Hardened.Simulators.slnx`, because `dotnet test --filter` is inert
on the pinned SDK with xunit.v3 and a trait filter would have started every emulator in the
coverage run.

## Failures

**`HRDF001` naming `[Stream]`**
There is no Google adapter for it. Use a queue with an ordering key if what is wanted is order per
key, and accept that it is not a replayable shard.

**A push is answered 500 and redelivered forever**
No handler declares the subscription the push names. The subscription's name is the route, so
rename one of them. A push from a subscription nothing serves is meant to fail, so Pub/Sub reports
it rather than the service discarding it.

**A timer is answered 500 with "posting to another timer's URL"**
The job's name and the URL's last segment disagree. Either is the timer's name, and the adapter
refuses to guess which.

**`[CloudRunTesting]` fails saying `[WebTesting]` is missing**
The envelope delivery posts to the test's web host, and `[WebTesting]` is what registers it.

**A `[Get]` beside a `[Queue]` trips `HRDF001` inside the repository**
The project imports the adapter's `.targets` and not the host's. `Hardened.Gcp.CloudRun.Runtime.targets`
is what binds the web verbs.

**A request is answered 413**
The body is over 16 MiB. Pub/Sub does not send one that large, so the front door does not read
one.
