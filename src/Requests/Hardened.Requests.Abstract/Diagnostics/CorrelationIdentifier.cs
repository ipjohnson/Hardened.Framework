using System.Diagnostics;

namespace Hardened.Requests.Abstract.Diagnostics;

/// <summary>
/// Where a request's correlation id comes from.
/// </summary>
/// <remarks>
/// <para>
/// <b>The trace id when there is one, a fresh one when there is not.</b> The pipeline already starts
/// a span per request and already joins the caller's trace when they sent a <c>traceparent</c>, so
/// when anything is collecting traces the correlation id and the trace id should be the same string
/// - two identities for one request is how a log line and a span end up impossible to line up.
/// </para>
/// <para>
/// But <c>ActivitySource.StartActivity</c> returns null when nothing is listening, which is
/// deliberate and is what makes instrumenting the pipeline unconditional. So on any deployment
/// without a collector - every developer machine, most test runs, plenty of production - there is no
/// trace id in existence, and that is exactly when someone reading logs most wants an id to group
/// them by. Hence the fallback.
/// </para>
/// <para>
/// <b>Shaped like a trace id either way.</b> <see cref="ActivityTraceId"/> generates it, so the
/// value is the same 32 hex characters whether it came from a trace or not, and nothing downstream
/// has to care which. It is also why this is not a <c>Guid</c>: a correlation id that is sometimes a
/// trace id and sometimes a GUID makes every log query two queries.
/// </para>
/// </remarks>
public static class CorrelationIdentifier {

    /// <summary>
    /// The current trace's id, or a new one when nothing is tracing.
    /// </summary>
    /// <remarks>
    /// Read lazily by the contexts rather than at construction, because the host builds the context
    /// before <c>IRequestLogger.RequestBegin</c> starts the span - so anything eager would mint an
    /// id and then be contradicted a moment later by a span carrying a different one.
    /// </remarks>
    public static string ForCurrentTrace() {
        var current = Activity.Current;

        return current is null
            ? ActivityTraceId.CreateRandom().ToHexString()
            : current.TraceId.ToHexString();
    }
}
