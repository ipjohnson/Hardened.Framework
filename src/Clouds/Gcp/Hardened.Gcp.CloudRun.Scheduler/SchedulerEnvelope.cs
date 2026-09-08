using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Gcp.CloudRun.Scheduler;

/// <summary>
/// A Cloud Scheduler job's request: the timer's name in the URL, the schedule in the headers.
/// </summary>
/// <remarks>
/// <para>
/// Scheduler sends whatever request the job was configured with - a POST by default, to a URL the
/// deployment chose - and adds <c>X-CloudScheduler: true</c>, <c>X-CloudScheduler-JobName</c> and,
/// for a cron schedule, <c>X-CloudScheduler-ScheduleTime</c>. The name a <c>[Timer]</c> declared
/// travels in the URL, <c>/_triggers/timer/{name}</c>, because that is the one thing the
/// deployment fully controls; the job-name header is cross-checked when it is present, and a job
/// posting to another timer's URL is refused rather than run under the wrong name.
/// </para>
/// <para>
/// Routes as <c>TIMER /nightly-rollup</c>. The body, usually empty, is handed on as it arrived,
/// so a job configured with one reaches a handler that binds it; every header of the delivery is
/// kept, so the schedule time is readable for deduplication across retries. Not a batch: a
/// schedule fires once.
/// </para>
/// </remarks>
public sealed class SchedulerEnvelope : ITriggerEnvelope {
    /// <summary>The scheme a schedule routes under.</summary>
    public const string TimerScheme = "TIMER";

    /// <summary>Where a job's target URL carries the timer's name by default.</summary>
    public const string DefaultPrefix = "/_triggers/timer/";

    /// <summary>Set to <c>true</c> on every request Scheduler sends.</summary>
    public const string MarkerHeader = "X-CloudScheduler";

    /// <summary>The job's name, which is cross-checked against the URL's when present.</summary>
    public const string JobNameHeader = "X-CloudScheduler-JobName";

    /// <summary>The scheduled time, RFC 3339, the same on every retry of one run.</summary>
    public const string ScheduleTimeHeader = "X-CloudScheduler-ScheduleTime";

    public SchedulerEnvelope(string prefix) {
        Prefix = TriggerHeaders.Prefix(prefix);
    }

    /// <summary>The path the timer's name is read under, rooted and ending in a slash.</summary>
    public string Prefix { get; }

    /// <summary>A request to the timer path, whatever its method: a job may be a GET.</summary>
    public bool Recognises(IExecutionRequest request) =>
        !string.IsNullOrEmpty(TriggerHeaders.Under(request.Path, Prefix));

    public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) {
        var name = TriggerHeaders.Under(request.Path, Prefix);

        if (string.IsNullOrEmpty(name)) {
            return null;
        }

        var job = TriggerHeaders.Get(request.Headers, JobNameHeader);

        if (!string.IsNullOrEmpty(job) &&
            !string.Equals(JobName(job!), name, StringComparison.Ordinal)) {
            // Refused rather than routed on either name: the job's target URL and the job's name
            // disagree, which is a wiring error in the deployment, and running a handler under a
            // name its schedule did not carry would hide it.
            throw new InvalidOperationException(
                $"Cloud Scheduler job '{job}' posted to the timer route '{name}'. The name in the " +
                $"target URL and the job's own name have to agree.");
        }

        return new CloudRunTriggerRequest(
            TimerScheme, "/" + name, payload.AsStream(), TriggerHeaders.Copy(request.Headers), request);
    }

    /// <summary>
    /// The job's own name, whether the header carries it bare or as the full resource name
    /// <c>projects/p/locations/l/jobs/nightly</c>.
    /// </summary>
    internal static string JobName(string job) {
        var slash = job.LastIndexOf('/');

        return slash > -1 ? job.Substring(slash + 1) : job;
    }
}
