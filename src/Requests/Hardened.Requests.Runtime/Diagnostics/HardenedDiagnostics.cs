using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Hardened.Requests.Runtime.Diagnostics;

/// <summary>
/// The <see cref="System.Diagnostics.ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/>
/// the request pipeline reports through.
/// </summary>
/// <remarks>
/// <para>
/// Both types are in the base class library on net8.0, so instrumenting against them adds no
/// dependency to any application that never collects any of it. OpenTelemetry is one consumer of
/// these, not the interface to them: an OTel SDK, Application Insights, Datadog, prometheus-net and
/// <c>dotnet-counters</c> all subscribe to the same two objects. That is why there is no
/// <c>Hardened.Telemetry.OpenTelemetry</c> package and no OTel type anywhere in this assembly.
/// </para>
/// <para>
/// Nothing here needs an AOT story of its own. ILC disables <c>EventSource</c>
/// (<c>System.Diagnostics.Tracing.EventSource.IsSupported=false</c>) but leaves <c>Activity</c>,
/// <c>ActivitySource</c> and <c>Meter</c> alone, so in-process listeners work in a native binary
/// exactly as they do on the JIT. Out-of-process collection - <c>dotnet-counters</c>,
/// <c>dotnet-trace</c>, <c>dotnet-monitor</c> - is what stops working, and an application that wants
/// it sets <c>&lt;EventSourceSupport&gt;true&lt;/EventSourceSupport&gt;</c>. Worth knowing because
/// AOT also rules out profiler-based auto-instrumentation: a framework that publishes native has to
/// instrument itself, which is what this is.
/// </para>
/// <para>
/// Neither object is disposed. Both are process-lifetime, and disposing an
/// <c>ActivitySource</c> stops it producing activities for the rest of the process.
/// </para>
/// </remarks>
public static class HardenedDiagnostics {
    /// <summary>
    /// The name to subscribe to, for both signals.
    /// </summary>
    /// <remarks>
    /// A published contract string: it is what an application passes to <c>AddSource</c> and
    /// <c>AddMeter</c>, and changing it silently stops collection for everyone who already
    /// configured it. There is a test asserting this exact value for that reason.
    /// </remarks>
    public const string SourceName = "Hardened.Requests";

    private static readonly string? AssemblyVersion =
        typeof(HardenedDiagnostics).Assembly.GetName().Version?.ToString();

    /// <summary>
    /// Where request spans come from.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Diagnostics.ActivitySource.StartActivity(string, ActivityKind)"/> returns
    /// <c>null</c> when nothing is listening, so the pipeline can instrument unconditionally and an
    /// application that collects nothing pays for a null check. That is what makes it reasonable
    /// for this to live in the runtime rather than behind a flag or an opt-in package.
    /// </remarks>
    public static readonly ActivitySource ActivitySource = new(SourceName, AssemblyVersion);

    /// <summary>
    /// Where request measurements come from.
    /// </summary>
    /// <remarks>
    /// A static instance rather than <c>IMeterFactory</c>, which would give per-container isolation
    /// but lives in Microsoft.Extensions.Diagnostics.Abstractions - a package, and the whole point
    /// of this file is that there is not one.
    /// </remarks>
    public static readonly Meter Meter = new(SourceName, AssemblyVersion);
}
