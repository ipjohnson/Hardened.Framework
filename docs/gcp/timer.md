# Timers

`[Timer("nightly")]` on a method makes the method the handler for the Cloud Scheduler job named
`nightly`. Each run of the job is one HTTP request to the service's path `/_triggers/timer/nightly`,
and the handler runs once for it.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Timer("nightly")]
    public void OnNightly() => log.Sweep();
}
```

In the template, `OrderLog` is a `[SingletonService]` that counts the runs in `Sweeps`. `Sweep()`
adds one. The application class names `[CloudRunRuntime]` and no adapter module:

```csharp
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[CloudRunRuntime]
public partial class Application;
```

A reference to the `Hardened.Gcp.CloudRun.Scheduler` package makes `[Timer]` mean a Cloud Scheduler
job. The build registers the package's `SchedulerModule` on the application.

The same handler and application class also run as a Cloud Functions 2nd gen function. The function
answers with the statuses in the tables and the exchange below. Its handler receives the same
headers as on Cloud Run.

[Triggers](/guide/triggers) covers `[Timer]` and the other trigger attributes.

## Packages

A timer service references `Hardened.Gcp.CloudRun.Scheduler` beside
`Hardened.Gcp.CloudRun.Runtime`:

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Gcp.CloudRun.Scheduler" Version="0.0.0-HARDENED-VERSION" />
```

The other packages, `Program.cs` and the project settings are the same for every Google Cloud
service. The Google Cloud [Overview](/gcp/) covers them. A Cloud Functions 2nd gen function
references the same adapter package. [Web services](/gcp/web) covers the function's own packages.

This command writes the service above and a test project:

```bash
dotnet new hardened-function -n Orders --host gcp --trigger timer
```

## Requests that reach the handler

The adapter reads a request as a run of a timer when the request's path starts with
`/_triggers/timer/` and a name follows. The rest of the path is the timer's name. The request can
use any HTTP method. `/_triggers/timer/nightly` reaches `[Timer("nightly")]` under the route
`TIMER /nightly`. A trailing slash on the path is dropped. A query string does not reach the
handler. One service can serve several timers, with a handler for each.

Cloud Scheduler sends the header `X-CloudScheduler-JobName` with the job's name on every request
([REST Resource: projects.locations.jobs](https://docs.cloud.google.com/scheduler/docs/reference/rest/v1/projects.locations.jobs)).
When a request carries the header, the job's name must be the timer's name. The header can hold the
name alone or the job's full resource name, such as
`projects/my-project/locations/us-central1/jobs/nightly`. The last segment of the resource name is
the job's name. A request whose job name differs answers 500. For the job `weekly` posting to
`/_triggers/timer/nightly`, the service logs this message:

```text
Cloud Scheduler job 'weekly' posted to the timer route 'nightly'. The name in the target URL and the job's own name have to agree.
```

A request without `X-CloudScheduler-JobName`, such as one sent by hand, reaches the timer that its
path names. A job's name holds only letters, digits, hyphens and underscores. A timer that a job
runs has a name of those characters too.

The timer's name matches exactly, including case. A request for a timer that no handler names
answers 500. For `/_triggers/timer/weekly`, with no `[Timer("weekly")]`, the service logs this
message:

```text
No handler is registered for TIMER /weekly. An event source is wired to this function that no trigger attribute declared.
```

A request outside `/_triggers/timer/`, or with no name after it, goes to the service's web routes.
The web routes answer 404. The adapter matches `/_triggers/timer/` exactly, including case.
`[SchedulerModule(Prefix = ...)]` serves the timers under another path, as
[Changing the path](#changing-the-path) shows.

The table lists which requests reach `[Timer("nightly")]`:

| Request | Reaches `[Timer("nightly")]` |
|---|---|
| `POST /_triggers/timer/nightly` from the job `nightly` | Yes |
| `GET /_triggers/timer/nightly` from the job `nightly` | Yes |
| `POST /_triggers/timer/nightly` with no `X-CloudScheduler-JobName` | Yes |
| `POST /_triggers/timer/nightly` from the job `weekly` | No. 500 |
| `POST /_triggers/timer/Nightly` | No. 500 |
| `POST /_triggers/timer/weekly`, with no `[Timer("weekly")]` | No. 500 |
| `POST /_Triggers/timer/nightly` | No. The web routes answer 404 |

## Body and headers

The job's body, when the job has one, is the request body as it arrived. A `[Timer]` handler usually
takes no payload parameter. A handler with no payload parameter ignores the body. A handler with a
payload parameter binds the body as JSON, whatever its `Content-Type`. [Triggers](/guide/triggers)
covers payload binding.

A handler with a payload parameter answers 400 to a run with no body. For `[Timer("report")]` on a
handler that takes an `Order`, the log line starts with this text:

```text
TIMER /report refused with 400: The input does not contain any JSON tokens.
```

Every header of the job's request reaches the handler under its own name. Header names match without
regard to case. The table lists the headers that Cloud Scheduler sets.
[REST Resource: projects.locations.jobs](https://docs.cloud.google.com/scheduler/docs/reference/rest/v1/projects.locations.jobs)
documents them.

| Header | Cloud Scheduler sets it to |
|---|---|
| `X-CloudScheduler` | `true` |
| `X-CloudScheduler-JobName` | The job's name |
| `X-CloudScheduler-ScheduleTime` | The time the run was scheduled for, in RFC 3339, when the job has a unix-cron schedule. The same on every retry of one run |
| `User-Agent` | `Google-Cloud-Scheduler` |
| `Content-Type` | `application/octet-stream`, when the job has a body and sets no `Content-Type` |

A handler reads the headers through an `IExecutionRequest` parameter. The interface is in the
namespace `Hardened.Requests.Abstract.Execution`. `X-CloudScheduler-ScheduleTime` can be missing, so
the handler below reads it with `TryGetValue`. Cloud Scheduler sends the header only for a job with
a unix-cron schedule. Under `[FunctionTesting]` alone, a request has no headers, as
[Testing a timer](#testing-a-timer) describes.

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
        request.Headers.TryGetValue("X-CloudScheduler-ScheduleTime", out var scheduled);

        logger.LogInformation("Sweep scheduled for {ScheduleTime}", scheduled.ToString());

        log.Sweep();
    }
}
```

Cloud Scheduler sends this request for a run of the job `nightly` that is due at 02:00 UTC:

```http
POST /_triggers/timer/nightly
User-Agent: Google-Cloud-Scheduler
X-CloudScheduler: true
X-CloudScheduler-JobName: nightly
X-CloudScheduler-ScheduleTime: 2026-09-24T02:00:00Z

HTTP/1.1 200 OK
```

The handler logs `Sweep scheduled for 2026-09-24T02:00:00Z`. The Google Cloud [Overview](/gcp/)
shows how to run the service locally and post a request to it.

## Failed runs

A run fails when its handler throws, when its body does not bind to the handler's parameter, when
the job's name differs from the timer's, or when no handler names the timer. The request then
answers with the status in this table:

| What happened | The request answers | Cloud Scheduler records |
|---|---|---|
| The handler returned | 200, no body | A successful run |
| The handler threw | 500, `{"type":"ServerError","message":"The server could not complete this request.","details":""}` | A failed run |
| The job's body did not bind to the handler's parameter | 400, a `ValidationError` body naming the parameter | A failed run |
| The job's name is not the timer's name | 500, no body | A failed run |
| No handler names the timer | 500, no body | A failed run |

Cloud Scheduler counts a response from 200 to 299 as a successful run
([REST Resource: projects.locations.jobs](https://docs.cloud.google.com/scheduler/docs/reference/rest/v1/projects.locations.jobs)).
Any other status is a failed run. A run with no response by the attempt deadline fails too. The
attempt deadline for an HTTP target is 3 minutes by default. A job can set it from 15 seconds to 30
minutes.

A job does not retry a failed run by default
([Retry jobs](https://docs.cloud.google.com/scheduler/docs/configuring/retry-jobs)).
`--max-retry-attempts` sets how many times Cloud Scheduler retries a failed run, from 0 to 5
([gcloud scheduler jobs create http](https://docs.cloud.google.com/sdk/gcloud/reference/scheduler/jobs/create/http)).
By default, the retries back off from 5 seconds to at most 1 hour. Retries that run past the job's
next scheduled time can make Cloud Scheduler skip that run.

::: warning
A job created without `--max-retry-attempts` does not retry a run whose handler failed. The run is
lost, and the job waits for its next scheduled time.
:::

Cloud Scheduler never runs two executions of one job at once. A run whose time comes while the run
before it is still running starts when that run ends.

A job can run more than once for one scheduled time
([About Cloud Scheduler](https://docs.cloud.google.com/scheduler/docs/overview)). The job's name and
`X-CloudScheduler-ScheduleTime` identify a run. The header is the same on every retry of the run.

A timer request carries no batch, so the adapter has no report of failed runs. `SchedulerModule` has
no setting that reports failures.

## Changing the path

`SchedulerModule` has one setting, `Prefix`. The prefix is the path under which a job's URI carries
the timer's name. It is `/_triggers/timer/` when unset. An application sets it by applying
`[SchedulerModule]` to its application class. The module is in the namespace
`Hardened.Gcp.CloudRun.Scheduler`.

```csharp
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Gcp.CloudRun.Scheduler;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[CloudRunRuntime]
[SchedulerModule(Prefix = "/jobs/")]
public partial class Application;
```

With `Prefix = "/jobs/"`, `/jobs/nightly` reaches `[Timer("nightly")]`. A request to
`/_triggers/timer/nightly` then goes to the web routes. The web routes answer 404. A prefix gets a
leading and a trailing slash when it lacks them, so `"jobs"` is `/jobs/`.

The build does not register the module a second time when the application applies it.
[Triggers](/guide/triggers) covers the rule. Under `[CloudRunTesting]`, a timer call ignores
`Prefix`, as [Testing a timer](#testing-a-timer) shows.

## Creating the job

A Cloud Scheduler job with an HTTP target runs the timer
([Running services on a schedule](https://docs.cloud.google.com/run/docs/triggering/using-scheduler)).
The job's URI is the service's URL followed by `/_triggers/timer/` and the timer's name. The job's
name is the timer's name. `gcloud run services describe orders --format 'value(status.url)'` prints
the service's URL
([Invoke with an HTTPS Request](https://docs.cloud.google.com/run/docs/triggering/https-request)).
A Cloud Functions 2nd gen function is a Cloud Run service. A job reaches the function at its URL the
same way.

The job calls the service with an OIDC token for a service account. The account needs the Cloud Run
Invoker role, `roles/run.invoker`, on the service. The token's audience is the service's URL.
`--oidc-token-audience` sets it. Without the flag, the audience is the job's whole URI, including
the path
([Authenticating service-to-service](https://docs.cloud.google.com/run/docs/authenticating/service-to-service)).

The job sends a `POST` by default
([gcloud scheduler jobs create http](https://docs.cloud.google.com/sdk/gcloud/reference/scheduler/jobs/create/http)).
`--schedule` takes a unix-cron expression, read in the time zone that `--time-zone` names. The time
zone is `Etc/UTC` by default. `--max-retry-attempts` sets the retries that
[Failed runs](#failed-runs) describes.

In these commands, `orders` is the deployed service and `https://orders-abc123-uc.a.run.app` is its
URL. `my-project` is the project, and `scheduler-invoker` is a service account of your choosing.

```bash
gcloud run services add-iam-policy-binding orders --region=us-central1 \
    --member=serviceAccount:scheduler-invoker@my-project.iam.gserviceaccount.com \
    --role=roles/run.invoker

gcloud scheduler jobs create http nightly \
    --location=us-central1 \
    --schedule="0 2 * * *" \
    --uri="https://orders-abc123-uc.a.run.app/_triggers/timer/nightly" \
    --oidc-service-account-email=scheduler-invoker@my-project.iam.gserviceaccount.com \
    --oidc-token-audience="https://orders-abc123-uc.a.run.app" \
    --max-retry-attempts=3
```

The first command sends `setIamPolicy` for the service `orders`, with a binding of
`roles/run.invoker` for `serviceAccount:scheduler-invoker@my-project.iam.gserviceaccount.com`. The
second sends `POST /v1/projects/my-project/locations/us-central1/jobs` with the job
`projects/my-project/locations/us-central1/jobs/nightly`. The job has the schedule `0 2 * * *` in
`Etc/UTC`. Its HTTP target has the method `POST` and the URI above. The target carries an OIDC
token for the service account, with the service's URL as its audience. The job's retry
configuration has 3 retries, 5 seconds to 3600 seconds of backoff and 5 doublings.

Deploying the service itself is the same for every trigger. The Google Cloud [Overview](/gcp/)
covers it. [Web services](/gcp/web) covers deploying a function.

## Testing a timer

`dotnet new hardened-function --host gcp --trigger timer` writes a test that runs the handler
through `Application.Timers`. [Testing functions](/guide/testing-functions) shows the test and
covers the façades.

The template's test project declares `[assembly: CloudRunTesting]` and `[assembly: WebTesting]`.
`[CloudRunTesting]` sends each call to a timer as the request Cloud Scheduler sends, and posts it to
the test's host. The request is a `POST` to `/_triggers/timer/` and the timer's name, with no body.
It carries these headers:

| Header | Value |
|---|---|
| `X-CloudScheduler` | `true` |
| `X-CloudScheduler-JobName` | The timer's name |
| `X-CloudScheduler-ScheduleTime` | `2026-01-01T00:00:00.000Z` |
| `User-Agent` | `Google-Cloud-Scheduler` |

Under `[FunctionTesting]` alone, the request has no headers and an empty body. The handler that
reads `X-CloudScheduler-ScheduleTime` passes the template's test under both.

A timer whose handler takes a payload has a façade method that takes messages, such as
`timers.Report(order)`. `[CloudRunTesting]` sends each message as one request, with the message as
a JSON body.

Under `[CloudRunTesting]`, a call whose handler throws fails with the handler's exception. A timer
call under `[CloudRunTesting]` posts to `/_triggers/timer/` whatever `Prefix` the application sets.
With `[SchedulerModule(Prefix = "/jobs/")]`, a call such as `timers.Tick()` fails with this message:

```text
The source would not read the answer to TIMER /tick as an acknowledgement: the service answered 404. It said: 
```

The Google Cloud [Testing](/gcp/testing) page covers `[CloudRunTesting]`.

## Next

| Page | Covers |
|---|---|
| [Invocations](/gcp/invoke) | The other request whose route is in its path |
| [Triggers](/guide/triggers) | The trigger attributes, source names and payload binding |
| [Overview](/gcp/) | The packages, `Program.cs`, running locally and deploying a service |
| [Web services](/gcp/web) | Running the same application as a Cloud Functions 2nd gen function |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |
