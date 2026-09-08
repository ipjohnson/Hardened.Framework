# Streams

A stream handler receives one event at a time, in the order one partition delivered them:

```csharp
using Hardened.Functions.Runtime.Attributes;

public class TelemetryHandlers {

    [Stream("readings")]
    public void OnReading(Reading reading, IReadingStore store) => store.Append(reading);
}
```

## Packages

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.1.0" />
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.30.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.EventHubs" Version="0.30.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.SourceGenerator" Version="0.30.0-rc1000" PrivateAssets="all" />
```

`dotnet new hardened-function --host azure --trigger stream` writes this shape with tests.

## The function

`[Stream("readings")]` becomes `Stream_readings`, bound by
`[EventHubTrigger("readings", Connection = "AzureWebJobsEventHubs", IsBatched = true)]` and
routed as `STREAM /readings`. The Event Hubs extension has no default connection of its own, and
a trigger without one fails the host at startup, so the generator always writes one:
`AzureWebJobsEventHubs`, named the way the Service Bus extension names its default. The
consumer group is the extension's default, `$Default`, and both are module properties:

```csharp
[HardenedModule]
[EventHubsModule(Connection = "TelemetryHub", ConsumerGroup = "readings-service")]
public partial class Application;
```

A function app reading a hub other applications also read needs a consumer group of its own;
sharing `$Default` between two readers is not a thing Event Hubs allows.

## What the handler sees

The event's body is the request body, and `Content-Type` is the event's content type when the
publisher set one. The event's properties become headers under their own names. Beside them, what
the hub stamped on the event:

| Header | Carries |
|---|---|
| `x-azure-eventhubs-sequence-number` | The event's position in the partition |
| `x-azure-eventhubs-offset` | The event's offset, which is what a checkpoint is made of, as the service wrote it |
| `x-azure-eventhubs-partition-key` | The publisher's partition key, when it set one |
| `x-azure-eventhubs-enqueued-time` | When the hub accepted the event, ISO 8601 |
| `x-azure-eventhubs-message-id` | The publisher's message id, when it set one |

The offset is a string rather than a number because the service's is: the emulator and a
geo-replicated namespace write offsets such as `0-128`.

## What a failure means

The batch is ordered, and the pipeline stops at the first failure rather than running the later
events, because applying a later event before the replay of an earlier one would break the order
the partition exists to keep. The failed invocation is what the host logs and counts.

**What the host does next is not what Kinesis does.** The Functions host advances the partition's
checkpoint when the invocation completes, with or without an exception. A thrown batch is not
delivered again unless the function app declares a retry policy, and then only until that policy
is spent. Kinesis on Lambda retries a failed batch until it succeeds or expires. A `[Stream]`
handler that has to see every event therefore needs a retry policy on Azure and a dead-letter
path of its own, and Microsoft's own guidance for the Event Hubs trigger says the same.

That is `Checkpoint` in the framework's
[vocabulary](/guide/triggers#batches-and-what-a-failure-means) with nothing to report to: the
host reads no batch report from an Event Hubs function, so `ReportsItemFailures` is false and
cannot be turned on.

## Retrying a failed batch

The retry policy is written on the module, and it applies to every stream function in the
application:

```csharp
[HardenedModule]
[EventHubsModule(RetryCount = 5, RetryDelay = "00:00:10")]
public partial class Application;
```

The generator writes it as the worker's `[FixedDelayRetry(5, "00:00:10")]` on the function and
into the metadata the host indexes, so the host invokes a thrown batch again up to five times,
ten seconds apart, before it advances the checkpoint past it. The two properties go together;
one without the other is `HRDAZ003`, naming the missing one. A batch that is still failing when
the policy is spent is not delivered again, so a handler that has to see every event needs a
dead-letter path of its own beside the policy.

## Deploying

```bash
az eventhubs eventhub create --namespace-name telemetry-ns --resource-group telemetry --name readings
az eventhubs eventhub consumer-group create --namespace-name telemetry-ns --resource-group telemetry \
    --eventhub-name readings --name readings-service
az functionapp config appsettings set --name telemetry --resource-group telemetry \
    --settings "AzureWebJobsEventHubs=$(az eventhubs namespace authorization-rule keys list \
        --namespace-name telemetry-ns --resource-group telemetry --name RootManageSharedAccessKey \
        --query primaryConnectionString --output tsv)"
```

Locally, the Event Hubs emulator answers the same setting with `UseDevelopmentEmulator=true`, and
the function app's own storage account, Azurite locally, is where the checkpoints go.

## Testing

```csharp
[HardenedTest]
public async Task ReadingsArriveInOrder(Application.Streams streams, IReadingStore store) {
    await streams.Readings(new Reading { Sequence = 1 }, new Reading { Sequence = 2 });

    Assert.Equal([1, 2], store.Sequences);
}
```

Several payloads are one batch. Under `[assembly: AzureFunctionsTesting]` the batch is the
`EventData[]` the worker would bind, built through the SDK's model factory with sequence numbers
and offsets, so the headers are there to assert on. See [Testing Azure handlers](/azure/testing).

## Next

- [Changes](/azure/change): the other ordered log, with the same failure rule
- [Triggers](/guide/triggers): the vocabulary
