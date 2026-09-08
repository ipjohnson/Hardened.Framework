# Hardened1

#if (aws)
An AWS Lambda function on [Hardened](https://github.com/ipjohnson/Hardened.Framework) — a
compile-time, source-generated .NET framework. The entry point AWS calls, the payload
deserialisation and the dependency injection are all written during the build.
#endif
#if (gcp)
A Google Cloud Run service on [Hardened](https://github.com/ipjohnson/Hardened.Framework) — a
compile-time, source-generated .NET framework. The dispatch, the payload deserialisation and the
dependency injection are all written during the build, and the trigger reaches the handler as
the HTTP request Google sends.
#endif

## Run it

```bash
dotnet build
dotnet test
dotnet run --project src/Hardened1
```

#if (aws)
The tests are how this function is exercised most of the time: they invoke it through the real
pipeline, with no AWS account and nothing to deploy.

`dotnet run`, or F5, runs the function the way the Lambda service runs it. There is no Lambda to
start it, so `Program.cs` starts the
[AWS Lambda Test Tool](https://github.com/aws/aws-lambda-dotnet/tree/master/Tools/LambdaTestTool-v2)
beside it and points the bootstrap at it. Invoke the function from the tool's page at
<http://localhost:5050> with a payload of your own; `HARDENED_LAMBDA_EMULATOR_PORT` moves the
page. The tool is pinned in `.config/dotnet-tools.json` and restored by the build. A deployed
function sets `AWS_LAMBDA_RUNTIME_API`, and then none of this runs.
#endif
#if (gcp)
The tests are how this function is exercised most of the time: they invoke it through the real
pipeline, with no Google account and nothing to deploy.

`dotnet run`, or F5, runs the service the way Cloud Run runs it: Kestrel on `PORT`, 8080 when it
is unset. A trigger is an HTTP request, so one can be posted by hand:

```bash
#if (invoke)
curl -X POST localhost:8080/_triggers/invoke/Process -H 'Content-Type: application/json' \
     -d '{"id":"A-1","quantity":2}'
#endif
#if (queue || topic)
curl -X POST localhost:8080/ -H 'Content-Type: application/json' \
     -d '{"message":{"data":"eyJpZCI6IkEtMSIsInF1YW50aXR5IjoxfQ==","messageId":"1"},"subscription":"projects/p/subscriptions/orders"}'
#endif
#if (timer)
curl -X POST localhost:8080/_triggers/timer/nightly -H 'X-CloudScheduler: true'
#endif
#if (change || blob)
# Post the CloudEvent Eventarc sends, in binary mode: ce-* headers and the data as the body.
#endif
```

## Deploy it

```bash
gcloud run deploy hardened1 --source . --region us-central1 --no-allow-unauthenticated
```

`--source` builds the `Dockerfile` with Cloud Build and deploys the image. Then wire the source to
the service:

```bash
#if (invoke)
curl -X POST "$(gcloud run services describe hardened1 --region us-central1 --format 'value(status.url)')/_triggers/invoke/Process" \
     -H "Authorization: Bearer $(gcloud auth print-identity-token)" \
     -H 'Content-Type: application/json' -d '{"id":"A-1","quantity":2}'
#endif
#if (queue)
gcloud pubsub subscriptions create orders --topic=orders \
    --push-endpoint="$(gcloud run services describe hardened1 --region us-central1 --format 'value(status.url)')/" \
    --push-auth-service-account=pubsub-pusher@PROJECT.iam.gserviceaccount.com
#endif
#if (topic)
gcloud eventarc triggers create orders --location=us-central1 --destination-run-service=hardened1 \
    --event-filters="type=google.cloud.pubsub.topic.v1.messagePublished" \
    --transport-topic=projects/PROJECT/topics/orders \
    --service-account=eventarc-invoker@PROJECT.iam.gserviceaccount.com
#endif
#if (timer)
gcloud scheduler jobs create http nightly --location=us-central1 --schedule="0 2 * * *" \
    --uri="$(gcloud run services describe hardened1 --region us-central1 --format 'value(status.url)')/_triggers/timer/nightly" \
    --http-method=POST --oidc-service-account-email=scheduler-invoker@PROJECT.iam.gserviceaccount.com
#endif
#if (change)
gcloud eventarc triggers create orders --location=nam5 --destination-run-service=hardened1 \
    --destination-run-region=us-central1 \
    --event-filters="type=google.cloud.firestore.document.v1.written" \
    --event-filters="database=(default)" --event-filters-path-pattern="document=orders/{id}" \
    --event-data-content-type="application/protobuf" \
    --service-account=eventarc-invoker@PROJECT.iam.gserviceaccount.com
#endif
#if (blob)
gcloud eventarc triggers create uploads --location=us-central1 --destination-run-service=hardened1 \
    --event-filters="type=google.cloud.storage.object.v1.finalized" --event-filters="bucket=uploads" \
    --service-account=eventarc-invoker@PROJECT.iam.gserviceaccount.com
#endif
```

There is no infrastructure package; the two commands above are the deployment.
#endif

## The two projects

| | |
|---|---|
| `src/Hardened1` | The function: its handler, its models and its services. |
#if (aws)
| `tests/Hardened1.Tests` | Tests, which invoke the function the way Lambda does. |
#endif
#if (gcp)
| `tests/Hardened1.Tests` | Tests, which deliver to the function the way Cloud Run does. |
#endif

#if (aws)
There is no separate host project. On Lambda the deployed artifact is this assembly, and
`Program.cs` is the entry point the runtime starts — so the split that a web application makes
between a library and a host has nothing to separate here.
#endif
#if (gcp)
There is no separate host project. On Cloud Run the deployed artifact is the container the
`Dockerfile` builds from this assembly, and `Program.cs` is the entry point it starts — so the
split that a web application makes between a library and a host has nothing to separate here.
#endif

## The handler

A method of a plain class carrying the trigger. No base type, no interface, no registration:

```csharp
public class OrderHandler(OrderLog log) {

#if (invoke)
    [HardenedFunction]
    public OrderAccepted Process(Order order) { ... }
#endif
#if (queue)
    [Queue("orders")]
    public void OnOrder(Order order) { ... }
#endif
#if (topic)
    [Topic("orders")]
    public void OnOrder(Order order) { ... }
#endif
#if (timer)
    [Timer("nightly")]
    public void OnNightly() { ... }
#endif
#if (change)
    [Change("orders")]
    public void OnOrderChanged(Order order) { ... }
#endif
#if (stream)
    [Stream("orders")]
    public void OnOrder(Order order) { ... }
#endif
#if (blob)
    [Blob("uploads")]
    public void OnUpload(Upload upload) { ... }
#endif
}
```

The generator writes a dispatch bound to that exact signature. The payload is deserialised into
the parameter, the dependencies come from the container, and a missing registration is a build
error rather than something the first invocation discovers.

#if (invoke)
The return value is serialised back as the invocation's response.
#endif
#if (delivery)
The handler is called once per item the source delivered. Returning normally handles that item;
throwing fails the invocation, which is what makes the source redeliver.
#endif

A service is registered next to the class it belongs to, with `[SingletonService]`,
`[ScopedService]` or `[TransientService]` — the module lists nothing, so it cannot fall out of step.

## Changing the trigger, and changing the cloud

`src/Hardened1/Application.cs` is the whole of the application declaration:

#if (aws)
```csharp
[HardenedModule]
public partial class Application;
```

There is no host module attribute here, and that is the point. The adapter, its serializer and the
filters it needs all arrive because the handler carries a trigger attribute: the generator reads the
build property the host package declares and registers what serves it.

So moving to another cloud is a package reference. Nothing in this file names one, and the handler
names a source rather than a service — `[Queue("orders")]` is a queue on whichever provider the
project references, and the same handler compiles against all of them.
#endif
#if (gcp)
```csharp
[HardenedModule]
[CloudRunRuntime]
public partial class Application;
```

The host is the one thing named here, because a Cloud Run service is a container listening on a
port and that is a fact about the deployment. There is no adapter module attribute, and that is
the point: the envelope, its filters and the front door that recognises it all arrive because the
handler carries a trigger attribute, through the build property the adapter package declares.

So moving to another cloud is this attribute and a package reference. The handler names a source
rather than a service — `[Queue("orders")]` is a queue on whichever provider the project
references, and the same handler compiles against all of them.
#endif

## Testing

`[HardenedTest]` boots the real application — the module graph, configuration and startup services —
and injects what the test asks for. The façade it takes is generated from the handler's own
attribute, so a renamed source or a changed payload is a compile error here rather than a test that
passes against nothing:

```csharp
#if (queue)
[HardenedTest]
public async Task AMessageReachesTheHandler(Application.Queues queues, OrderLog log) {
    await queues.Orders(new Order { Id = "A-1", Quantity = 2 });

#if (xunit)
    Assert.Equal("A-1", Assert.Single(log.Orders).Id);
#else
    Assert.That(log.Orders.Select(order => order.Id), Is.EqualTo(new[] { "A-1" }));
#endif
}
#endif
#if (invoke)
[HardenedTest]
public async Task ThePayloadReachesTheHandler(Application.Invocations invocations, OrderLog log) {
    var accepted = await invocations.Process(new Order { Id = "A-1", Quantity = 2 });

#if (xunit)
    Assert.Equal("A-1", accepted.Id);
#else
    Assert.That(accepted.Id, Is.EqualTo("A-1"));
#endif
}
#endif
#if (topic)
[HardenedTest]
public async Task ANotificationReachesTheHandler(Application.Topics topics, OrderLog log) {
    await topics.Orders(new Order { Id = "A-1", Quantity = 2 });

#if (xunit)
    Assert.Equal("A-1", Assert.Single(log.Orders).Id);
#else
    Assert.That(log.Orders.Select(order => order.Id), Is.EqualTo(new[] { "A-1" }));
#endif
}
#endif
#if (timer)
[HardenedTest]
public async Task TheScheduleReachesTheHandler(Application.Timers timers, OrderLog log) {
    await timers.Nightly();

#if (xunit)
    Assert.Equal(1, log.Sweeps);
#else
    Assert.That(log.Sweeps, Is.EqualTo(1));
#endif
}
#endif
#if (change)
[HardenedTest]
public async Task AChangedRowReachesTheHandler(Application.Changes changes, OrderLog log) {
    await changes.Orders(new Order { Id = "A-1", Quantity = 2 });

#if (xunit)
    Assert.Equal("A-1", Assert.Single(log.Orders).Id);
#else
    Assert.That(log.Orders.Select(order => order.Id), Is.EqualTo(new[] { "A-1" }));
#endif
}
#endif
#if (stream)
[HardenedTest]
public async Task ARecordReachesTheHandler(Application.Streams streams, OrderLog log) {
    await streams.Orders(new Order { Id = "A-1", Quantity = 2 });

#if (xunit)
    Assert.Equal("A-1", Assert.Single(log.Orders).Id);
#else
    Assert.That(log.Orders.Select(order => order.Id), Is.EqualTo(new[] { "A-1" }));
#endif
}
#endif
#if (blob)
[HardenedTest]
public async Task ANotificationReachesTheHandler(Application.Blobs blobs, OrderLog log) {
    await blobs.Uploads(new Upload { Key = "report.pdf", Size = 1024 });

#if (xunit)
    Assert.Equal("report.pdf", Assert.Single(log.Uploads).Key);
#else
    Assert.That(log.Uploads.Select(upload => upload.Key), Is.EqualTo(new[] { "report.pdf" }));
#endif
}
#endif
```

That is the real pipeline — deserialisation, the filter chain, the handler — rather than a method
#if (aws)
call. With `[assembly: LambdaTesting]` in `Bootstrap.cs` it is also the real envelope: the payload
is packed the way AWS sends it and goes in through the invocation loop.
#endif
#if (gcp)
call. With `[assembly: CloudRunTesting]` in `Bootstrap.cs` it is also the real request: the payload
is packed the way Google sends it and posted to the test's host, front door and all.
#endif
#if (moq)
Take a `Mock<T>` parameter and that service is substituted for the whole container: the mock is Moq's,
through `[assembly: MoqSupport]` in `Bootstrap.cs`.
#endif
#if (nsubstitute)
Mark a parameter `[Mock]` and that service is substituted for the whole container: the substitute is
NSubstitute's, through `[assembly: NSubstituteSupport]` in `Bootstrap.cs`.
#endif
#if (fakeiteasy)
Mark a parameter `[Mock]` and that service is substituted for the whole container: the fake is
FakeItEasy's, through `[assembly: FakeItEasySupport]` in `Bootstrap.cs`.
#endif

## Reading the generated code

The fastest way to understand any of this is to read what the build wrote. It is ordinary C#, and
`EmitCompilerGeneratedFiles` is already on:

```
src/Hardened1/obj/Debug/net8.0/generated/
```

One directory per generator: the dispatch, the handler and the module registration are all there.

## Where to go next

- [Documentation](https://ipjohnson.github.io/Hardened.Framework)
- `AGENTS.md` in this directory — the invariants and gotchas, for anyone or anything editing the
  code rather than reading it
