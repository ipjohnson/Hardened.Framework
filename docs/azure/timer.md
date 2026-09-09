# Timers

A timer handler runs on a schedule the deployment decides:

```csharp
using Hardened.Functions.Runtime.Attributes;

public class Housekeeping {

    [Timer("nightly")]
    public Task Nightly(IReportBuilder reports) => reports.Build();
}
```

## Packages

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.1.0" />
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.32.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.Timer" Version="0.32.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.SourceGenerator" Version="0.32.0-rc1000" PrivateAssets="all" />
```

`dotnet new hardened-function --host azure --trigger timer` writes this shape with tests.

## The schedule is an app setting

`[Timer("nightly")]` becomes a function named `Timer_nightly` whose binding is
`[TimerTrigger("%Hardened:Timers:nightly%")]`. The host resolves the expression against its
settings, so the schedule is the app setting `Hardened:Timers:nightly` and nothing in the code
says when the handler runs. The same handler runs at two in the morning in production and every
minute in a test environment.

The value is an NCRONTAB expression with six fields, seconds first:

```bash
az functionapp config appsettings set --name orders --resource-group orders \
    --settings "Hardened__Timers__nightly=0 0 2 * * *"
```

Double underscores, because a colon is a hierarchy separator only on Windows and `__` is one on
both; the host reads either as `Hardened:Timers:nightly`. Locally the setting sits in
`local.settings.json`:

```json
"Hardened:Timers:nightly": "0 0 2 * * *"
```

A timer whose setting is missing fails the host at startup, naming the setting. That is the
host's own check, and it is the right one: a schedule nobody wrote is not a schedule.

## What the handler sees

The body is the timer's state as the host sends it, a JSON object with the schedule, the last and
next occurrences and whether the run is late; a handler that wants any of it declares a type with
those properties and binds the body. One that does not takes nothing. The one fact lifted into a
header:

| Header | Carries |
|---|---|
| `x-azure-timer-past-due` | `true` when the host is running an occurrence it missed |

A handler that would rather skip a late run than catch up reads the header and returns.

The host runs a timer on one instance at a time. The schedule's state lives in the function app's
storage account, which is why a timer function app needs `AzureWebJobsStorage` even when nothing
else does.

## What a failure means

A thrown exception fails the invocation, which the host records as a failed run against the
schedule; the next occurrence fires on time. There is no retry unless the function app declares a
retry policy, and no batch: one occurrence, one invocation.

## Testing

```csharp
[HardenedTest]
public async Task TheNightlyRunBuildsTheReport(Application.Timers timers, [Mock] IReportBuilder reports) {
    await timers.Nightly();

    await reports.Received().Build();
}
```

The façade is named for the timer and takes nothing. Under `[assembly: AzureFunctionsTesting]` the
delivery hands the invocation handler the JSON the host would send, so the past-due header is
there to assert on. See [Testing Azure handlers](/azure/testing).

## Next

- [Queues](/azure/queue): work a timer discovers is often best queued
- [Triggers](/guide/triggers): the vocabulary
