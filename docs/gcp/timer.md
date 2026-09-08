# Timers

A schedule carries nothing but the fact that it fired, so the handler takes no payload:

```csharp
using Hardened.Functions.Runtime.Attributes;

public class Rollups {

    [Timer("nightly-rollup")]
    public Task OnNightly(IRollupService rollups) => rollups.Run();
}
```

## Packages

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.30.0-rc1000" />
<PackageReference Include="Hardened.Gcp.CloudRun.Scheduler" Version="0.30.0-rc1000" />
```

`dotnet new hardened-function --host gcp --trigger timer` writes this shape with tests.

## The name is in the URL

Cloud Scheduler sends whatever request the job was configured with, to whatever URL the job names.
The timer's name travels in that URL, under `/_triggers/timer/`, because the URL is the one part of
the delivery the deployment fully controls:

```bash
gcloud scheduler jobs create http nightly-rollup \
    --location=us-central1 \
    --schedule="0 2 * * *" \
    --time-zone="Etc/UTC" \
    --uri="https://rollups-abc123-uc.a.run.app/_triggers/timer/nightly-rollup" \
    --http-method=POST \
    --oidc-service-account-email=scheduler-invoker@my-project.iam.gserviceaccount.com
```

Any method is accepted, because a job may be a GET. The request is routed as `TIMER
/nightly-rollup`.

Scheduler adds `X-CloudScheduler: true`, `X-CloudScheduler-JobName` and, for a cron schedule,
`X-CloudScheduler-ScheduleTime`. The job name is cross-checked against the URL when it is present,
and a job that names one timer while posting to another's URL is answered 500 rather than run
under the wrong name. Every header of the delivery reaches the handler, so
`X-CloudScheduler-ScheduleTime` is readable for deduplicating a retried run.

To put the timers somewhere other than `/_triggers/timer/`, write the module out:

```csharp
using Hardened.Gcp.CloudRun.Scheduler;

[HardenedModule]
[CloudRunRuntime]
[SchedulerModule(Prefix = "/jobs/")]
public partial class Application;
```

That is the one reason to write it out. Its registration otherwise arrives through `[Timer]`.

## What the handler sees

A body, if the job was configured with one, handed on as it arrived, so a job that posts JSON
reaches a handler that binds it. Most jobs post nothing and most handlers take nothing.

## What a failure means

A success answer completes the run. A thrown exception answers 500, and Scheduler retries according
to the job's retry configuration, with the same `X-CloudScheduler-ScheduleTime` on every attempt. A
schedule fires once, so there is no batch and nothing to report.

## Testing

```csharp
[HardenedTest]
public async Task TheScheduleReachesTheHandler(Application.Timers timers, RollupLog log) {
    await timers.NightlyRollup();

    Assert.Equal(1, log.Runs);
}
```

`Timers` for `[Timer]`, with a method per schedule and no parameter. Adding
`[assembly: CloudRunTesting]` posts the request Scheduler sends, with its headers, to the test's
host. See [Testing Cloud Run handlers](/gcp/testing).

## Next

- [Invocations](/gcp/invoke): the other request whose route is in the URL
- [Triggers](/guide/triggers): the vocabulary
