# Google Cloud Run

The handlers you wrote for Kestrel or for Lambda run on Cloud Run. What changes is a package
reference and the attribute that names the host.

```csharp
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
[CloudRunRuntime]
public partial class Application;

public class OrderHandler(OrderLog log) {

    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

A Cloud Run service is a container listening on a port, so the application names its host the way
a web application names `[KestrelRuntime]`. It names nothing else. The handler carries a
[trigger](/guide/triggers); the generator reads the build property the adapter package declares and
registers the module for you. Nothing in the application says Pub/Sub, so moving the queue to
another provider changes the host attribute and a package reference.

`dotnet new hardened-function --host gcp` and `dotnet new hardened-web --host cloud-run` write both
shapes with tests and a Dockerfile; see [Project templates](/guide/project-templates).

## The packages

One host package, and one adapter per source. An adapter is a package rather than a flag so a
service carries only the code that reads the envelopes it can receive. None of them references a
Google SDK: every delivery arrives as HTTP and is read as HTTP. The one exception is Firestore,
whose events are protobuf, and that is why it is its own package.

| Package | Serves |
|---|---|
| `Hardened.Gcp.CloudRun.Runtime` | The host. `[CloudRunRuntime]`, `CloudRunHost`, the front door and the web verbs. Every Cloud Run application references it |
| `Hardened.Gcp.CloudRun.PubSub` | `[Queue]` from a push subscription and `[Topic]` from an Eventarc trigger on a topic |
| `Hardened.Gcp.CloudRun.Scheduler` | `[Timer]` from a Cloud Scheduler job |
| `Hardened.Gcp.CloudRun.Invoke` | `[HardenedFunction]`, a direct invocation over HTTP |
| `Hardened.Gcp.CloudRun.Storage` | `[Blob]` from Cloud Storage, through Eventarc or a notification |
| `Hardened.Gcp.CloudRun.Firestore` | `[Change]` from Firestore, with `[OldValue]` |
| `Hardened.Gcp.CloudRun.Eventarc` | `[Event]`, any CloudEvent Eventarc delivers |
| `Hardened.Gcp.CloudRun` | Every adapter in one reference. `HRDF003` says which ones the service does not use |
| `Hardened.Gcp.CloudRun.Testing` | `[CloudRunTesting]`, the delivery that sends the real envelopes |

There is no `[Stream]` adapter, and there is not going to be one in this line. Google has no
sharded, checkpointed stream a `[Stream]` handler can be moved to; Pub/Sub ordering keys order
messages within a key but are not a shard a consumer replays from a position. A project that writes
`[Stream]` and references only these packages fails the build with `HRDF001`, which names the gap,
rather than deploying a handler nothing delivers to. See
[When nothing serves a trigger](/guide/triggers#when-nothing-serves-a-trigger).

## What the runtime gives you

**The front door.** Every trigger reaches a Cloud Run service as an HTTP request: a Pub/Sub push, a
CloudEvent from Eventarc, a Cloud Scheduler job's POST. A filter ahead of routing asks the adapters
the service references whether they recognise the request. One that does unwraps it into the
trigger route the handler declared, `QUEUE /orders`, and the request continues down the same
pipeline a web request takes. One that nothing recognises is routed as the web request it is.

**One dispatch.** A service can serve `[Get]` routes and `[Queue]` handlers on one socket. The host
composes the web dispatch and the function dispatch into one, so the two never disagree about a
request.

**The container contract.** `CloudRunHost.Listen` binds every interface on the port `PORT` names,
8080 when it is unset. `CloudRunHost.RunAsync` takes `SIGTERM`, stops accepting requests and gives
what is in flight ten seconds to finish, which is the grace Cloud Run gives before `SIGKILL`.

**The revision on the request.** `K_SERVICE`, `K_REVISION` and `K_CONFIGURATION` are on the
request's transport info as `faas.name`, `faas.version` and `gcp.cloud_run.configuration`, so a log
line or a metric can say which revision served the request.

## Queue or topic

Both `[Queue]` and `[Topic]` are Pub/Sub, and the two attributes mean two deployments.

`[Queue("orders")]` is a push subscription named `orders`. A push carries the subscription's name
and not the topic's, so the queue is routed on the subscription. One service consumes it, and a
message goes to one instance.

`[Topic("order-events")]` is an Eventarc trigger on the topic named `order-events`. Eventarc
delivers a CloudEvent whose source names the topic, so the topic is routed on the topic. Every
service with a trigger on it sees every message, which is what a topic means everywhere else.

That is a real difference in what the two attributes deploy as, and it is written on both pages
rather than hidden behind a naming convention on subscriptions. See [Queues](/gcp/queue) and
[Topics](/gcp/topic).

## Deploying

There is no infrastructure package. A service deploys from its Dockerfile, and the sources are
wired to it with `gcloud` afterwards:

```bash
gcloud run deploy orders --source . --region us-central1 --no-allow-unauthenticated
```

Each family's page shows the one command that wires its source to the service. The templates write
the Dockerfile.

## Where things are

| Area | Page | Source |
|---|---|---|
| The host, HTTP routes and the container contract | [Web services](/gcp/web) | [`Hardened.Gcp.CloudRun.Runtime`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Gcp/Hardened.Gcp.CloudRun.Runtime) |
| Queues | [Queues](/gcp/queue) | [`Hardened.Gcp.CloudRun.PubSub`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Gcp/Hardened.Gcp.CloudRun.PubSub) |
| Topics | [Topics](/gcp/topic) | [`Hardened.Gcp.CloudRun.PubSub`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Gcp/Hardened.Gcp.CloudRun.PubSub) |
| Schedules | [Timers](/gcp/timer) | [`Hardened.Gcp.CloudRun.Scheduler`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Gcp/Hardened.Gcp.CloudRun.Scheduler) |
| Direct invocation | [Invocations](/gcp/invoke) | [`Hardened.Gcp.CloudRun.Invoke`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Gcp/Hardened.Gcp.CloudRun.Invoke) |
| Objects | [Blobs](/gcp/blob) | [`Hardened.Gcp.CloudRun.Storage`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Gcp/Hardened.Gcp.CloudRun.Storage) |
| Documents | [Changes](/gcp/change) | [`Hardened.Gcp.CloudRun.Firestore`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Gcp/Hardened.Gcp.CloudRun.Firestore) |
| Any CloudEvent | [Events](/gcp/event) | [`Hardened.Gcp.CloudRun.Eventarc`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Gcp/Hardened.Gcp.CloudRun.Eventarc) |
| Test harnesses | [Testing Cloud Run handlers](/gcp/testing) | [`Hardened.Gcp.CloudRun.Testing`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Gcp/Hardened.Gcp.CloudRun.Testing) |

The trigger attributes themselves are not here. They live in `Hardened.Functions.Runtime`, which
names no cloud; see [Triggers](/guide/triggers). The CloudEvents reader the Eventarc adapters share
is `Hardened.CloudEvents`, which names no cloud either.
