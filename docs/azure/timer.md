# Timers

`[Timer("nightly")]` on a method makes it the handler for the Azure Functions timer function
`Timer_nightly`, whose schedule is the app setting `Hardened:Timers:nightly`. Each run of the
schedule is one invocation of the function, and the handler runs once for it.

The `hardened-function` template writes this handler in `src/Orders/OrderHandler.cs`, shown here
without its comments:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Timer("nightly")]
    public void OnNightly() => log.Sweep();
}
```

In the template, `OrderLog` is a `[SingletonService]` that counts runs in `Sweeps`. `Sweep()` adds
one.

The application class in `src/Orders/Application.cs` names no module:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

A reference to `Hardened.Azure.Functions.Timer` makes `[Timer]` an Azure Functions timer trigger.
The build registers the package's `TimerModule` on the application and writes the function.
[Triggers](/guide/triggers) covers `[Timer]` and the other trigger attributes.

## Packages

A timer function app references `Hardened.Azure.Functions.Timer` beside
`Hardened.Azure.Functions.Runtime`:

```xml
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Azure.Functions.Timer" Version="0.0.0-HARDENED-VERSION" />
```

Every Azure function app has the same other packages, among them
`Microsoft.Azure.Functions.Worker.Sdk` and `Hardened.Azure.Functions.SourceGenerator`. It also has
the same `Program.cs`, `host.json` and project settings. The Azure [Overview](/azure/) covers them.

This command writes the function app, its `local.settings.json` with the schedule setting, and a
test project:

```bash
dotnet new hardened-function -n Orders --host azure --trigger timer
```

## The function

The build writes one function for each `[Timer]` handler. For `[Timer("nightly")]`, it writes:

| Part | Value |
|---|---|
| Function | `Timer_nightly` |
| Trigger | A timer trigger that binds the timer's state as a string |
| Schedule | `%Hardened:Timers:nightly%` |
| The setting in `local.settings.json` | `Hardened:Timers:nightly` |
| The setting in a function app | `Hardened__Timers__nightly` |
| Route | `TIMER /nightly` |

The function's name is `Timer_` followed by the timer's name. Each character of the name that is not
a letter or a digit becomes `_`, so `[Timer("nightly-rollup")]` gives `Timer_nightly_rollup`.

The trigger's schedule is `%Hardened:Timers:nightly%`, so the host reads the schedule from the
setting `Hardened:Timers:nightly`. The setting's name and the route keep the timer's name as the
attribute writes it, so `[Timer("nightly-rollup")]` runs under `TIMER /nightly-rollup`.

One app can hold several timers, each with its own function and setting. Two timers whose names give
the same function name fail the build with `HRDAZ002`. The build compares function names without
regard to case, so `[Timer("nightly")]` and `[Timer("Nightly")]` collide too.

For `[Timer("nightly-rollup")]` on `OnRollup` and `[Timer("nightly_rollup")]` on `OnOtherRollup` in
one `OrderHandler`, `dotnet build` reports:

```text
error HRDAZ002: The handlers OrderHandler.OnRollup and OrderHandler.OnOtherRollup both produce the Azure function 'Timer_nightly_rollup', which the host would refuse as a duplicate. Rename one of their sources.
```

## The schedule

The setting holds an NCRONTAB expression of six fields. The first field is for seconds.
`0 0 2 * * *` runs at 02:00:00 every day. An expression of five fields, without seconds, works too.
A `TimeSpan` such as `01:00:00` sets the time between runs. A `TimeSpan` works only on an App Service
plan
([Timer trigger for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-timer)).

Under `func start`, the setting goes in the `Values` of `local.settings.json`. The template writes it
in `src/Orders/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "Hardened:Timers:nightly": "0 0 2 * * *",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AzureWebJobsStorage": "UseDevelopmentStorage=true"
  }
}
```

The Azure [Overview](/azure/) covers `FUNCTIONS_WORKER_RUNTIME`, `AzureWebJobsStorage` and running
the app locally. After its build output, `func start` lists the function:

```text
Functions:

	Timer_nightly: timerTrigger
```

`Hardened__Timers__nightly`, with double underscores, is the same setting. In a function app's
settings, Azure reads a double underscore as the separator on Windows and on Linux. Azure reads a
colon as the separator only on Windows
([App settings reference for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-app-settings)).

An App Service app setting's name holds only letters, digits, periods and underscores. On Linux, a
period in the name is written as `_`
([Configure an App Service App](https://learn.microsoft.com/en-us/azure/app-service/configure-common)).
A timer whose schedule is set in a function app therefore needs a name of letters, digits and
underscores.

When the setting is missing, the host disables that one function and logs:

```text
Function 'Functions.Timer_nightly' failed indexing and will be disabled.
The 'Timer_nightly' function is in error: Microsoft.Azure.WebJobs.Host: Error indexing method 'Functions.Timer_nightly'. Microsoft.Azure.WebJobs.Host: '%Hardened:Timers:nightly%' does not resolve to a value.
```

The host disables the function the same way when the value is not a schedule. For the value
`every night`, it logs:

```text
The schedule expression 'every night' was not recognized as a valid cron expression or timespan string.
```

In Azure, the host reads the expression in UTC, unless the app sets `WEBSITE_TIME_ZONE`. Azure does
not support `WEBSITE_TIME_ZONE` or `TZ` on Linux in the Consumption and Flex Consumption plans. Under
`func start`, the host reads the expression in the machine's time zone.

## What the handler receives

The request body is the timer's state as the host sends it. The body is a JSON object with
`Schedule`, `ScheduleStatus` and `IsPastDue`. For a `0 * * * * *` timer, the run due at 13:09:00 UTC
receives this body:

```json
{"Schedule":{"AdjustForDST":true},"ScheduleStatus":{"Last":"2026-09-24T13:08:00.0096035+00:00","Next":"2026-09-24T13:09:00+00:00","LastUpdated":"2026-09-24T13:08:00.0096035+00:00"},"IsPastDue":false}
```

`ScheduleStatus` holds `Last`, `Next` and `LastUpdated`. Their values depend on the run:

| Run | `ScheduleStatus.Last` | `ScheduleStatus.Next` | `IsPastDue` |
|---|---|---|---|
| On schedule | The previous run, or `0001-01-01T00:00:00+00:00` before the first | The time this run was due | `false` |
| At start, after a restart that missed runs | The last run before the stop | The first time the host missed | `true` |
| Fired through the admin endpoint | As the host last stored it | The next time on the schedule | `false` |
| On a schedule that runs more than once a minute | `ScheduleStatus` is null | `ScheduleStatus` is null | `false` |

The host does not monitor a schedule that runs more than once a minute, so `ScheduleStatus` is null
for it. A run fired through the admin endpoint has `IsPastDue` false, whatever its input says.

The request carries two headers:

| Header | Value |
|---|---|
| `Content-Type` | `application/json` |
| `x-azure-timer-past-due` | `true` or `false`, from the body's `IsPastDue` |

`x-azure-timer-past-due` is missing when the body has no `IsPastDue`, as under `[FunctionTesting]`
alone. Header names are matched without regard to case.

A handler reads the headers through an `IExecutionRequest` parameter, from the namespace
`Hardened.Requests.Abstract.Execution`. This handler reads the header with `TryGetValue`, because the
header can be missing:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Timer("nightly")]
    public void OnNightly(IExecutionRequest request)
    {
        request.Headers.TryGetValue("x-azure-timer-past-due", out var pastDue);

        logger.LogInformation("Sweep, past due: {PastDue}", pastDue.ToString());

        log.Sweep();
    }
}
```

A handler that takes a parameter of its own type binds the body into it.
[Triggers](/guide/triggers) covers payload binding. `src/Orders/TimerStatus.cs` declares a type for
the body:

```csharp
namespace Orders;

public class TimerStatus
{
    public bool IsPastDue { get; set; }

    public TimerScheduleStatus? ScheduleStatus { get; set; }
}

public class TimerScheduleStatus
{
    public DateTimeOffset Last { get; set; }

    public DateTimeOffset Next { get; set; }

    public DateTimeOffset LastUpdated { get; set; }
}
```

The handler in `src/Orders/OrderHandler.cs` takes it as a parameter:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Timer("nightly")]
    public void OnNightly(TimerStatus status)
    {
        logger.LogInformation("Sweep, previous run at {Last}", status.ScheduleStatus?.Last);

        log.Sweep();
    }
}
```

## Running the timer by hand

A POST to the host's admin endpoint fires the timer by hand. This exchange fires `Timer_nightly`
while the app runs under `func start`, which the Azure [Overview](/azure/) covers:

```http
POST /admin/functions/Timer_nightly HTTP/1.1
Host: localhost:7071
Content-Type: application/json

{"input":""}

HTTP/1.1 202 Accepted
```

The host answers 202 with no body and then runs the function. The handler that reads
`x-azure-timer-past-due` logs `Sweep, past due: false`. The path takes the function's name. A body of
`{}` works the same.

The host answers 404 for a name that no function has. For a function that a missing setting
disabled, the host answers 202. The function does not run.

Under `func start`, the endpoint needs no key. In Azure, it needs the app's master key in the
`x-functions-key` header
([Manually run a non HTTP-triggered Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-manually-run-non-http)).

## Failed and missed runs

A run fails when its handler throws. The invocation fails with the handler's exception. For a timer
named `failing-sweep`, the host logs `Executed 'Functions.Timer_failing_sweep' (Failed, ...)` with
the exception. A run is one invocation, with no batch and no report of failed items.

The timer trigger does not retry a failed run. The function runs again at the next time on its
schedule. The function the build writes declares no retry policy, and the module has no setting for
one.

A run that the host missed while the app was stopped runs once when the host starts, with
`IsPastDue` true. The timer then keeps to the schedule. When the app runs on several instances, one instance runs the timer. A run does not
start while the one before it is still running
([Timer trigger for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-timer)).

This table gives the invocation and the next run for each case:

| What happened | The invocation | The next run |
|---|---|---|
| The handler returned | Succeeds | At the next time on the schedule |
| The handler threw | Fails with the handler's exception | At the next time on the schedule. The failed run is not retried |
| The setting is missing, or is not a schedule | None. The function is disabled | None, until the setting is fixed and the app restarts |

::: warning
A function app deployed without the timer's schedule setting starts, runs its other functions, and
never runs the timer. The host logs the indexing error above and keeps running.
:::

## Module settings

The module has no settings, and an application does not need to declare it. The function the build
writes sets neither `RunOnStartup` nor `UseMonitor`, so the host's defaults apply. The timer
does not run when the host starts, apart from a missed run. The host monitors a schedule that runs
at most once a minute.

## Deploying

A function app reads the schedule from its app settings, as `Hardened__Timers__nightly`. This
command sets it on the function app `orders`, in the resource group `orders`:

```bash
az functionapp config appsettings set --name orders --resource-group orders \
    --settings "Hardened__Timers__nightly=0 0 2 * * *"
```

The CLI reads the app's settings. It then sends
`PUT /subscriptions/{subscription}/resourceGroups/orders/providers/Microsoft.Web/sites/orders/config/appsettings?api-version=2025-05-01`
with them and `"Hardened__Timers__nightly": "0 0 2 * * *"`.

Azure restarts the function app when an app setting changes
([App settings reference for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-app-settings)).

The timer keeps its schedule status and its lock in the host's storage account, which
`AzureWebJobsStorage` names. Without `AzureWebJobsStorage`, a timer whose schedule runs at most once
a minute does not start, under `func start` too. The host logs:

```text
The listener for function 'Functions.Timer_nightly' was unable to start. Microsoft.Azure.WebJobs.Extensions.Timers.Storage: Could not create BlobContainerClient for ScheduleMonitor.
```

A function app deploys the same way whatever its trigger. The Azure [Overview](/azure/) covers it.

## Testing

`dotnet new hardened-function --host azure --trigger timer` writes a test that runs the handler
through `Application.Timers`. [Testing functions](/guide/testing-functions) shows the test and covers
the façades.

The template's test project declares `[assembly: AzureFunctionsTesting]`. The attribute runs each
call to a timer as one invocation of the timer's function, with a body it writes:

| Field | Value |
|---|---|
| `ScheduleStatus.Last` | An hour before the call |
| `ScheduleStatus.LastUpdated` | An hour before the call |
| `ScheduleStatus.Next` | An hour after the call |
| `IsPastDue` | `false` |

A call that passes messages runs the handler once for each message. Each run gets that body, not the
message. A test cannot make `IsPastDue` true through the façade.

Under `[FunctionTesting]` alone, the request has no headers and an empty body. A handler that binds
the body, such as the `TimerStatus` handler above, is refused with a 400 and the message
`The input does not contain any JSON tokens`. The call returns normally.

Under `[AzureFunctionsTesting]`, a call whose handler throws fails with the handler's exception. The
Azure [Testing](/azure/testing) page covers the attribute.

## Next

| Page | Covers |
|---|---|
| [Triggers](/guide/triggers) | The trigger attributes, source names and payload binding |
| [Overview](/azure/) | The packages, the application class, the entry point, running locally and deploying |
| [Events](/azure/event) | Event Grid events, which one function serves |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |
| [Testing](/azure/testing) | What `[AzureFunctionsTesting]` builds |
