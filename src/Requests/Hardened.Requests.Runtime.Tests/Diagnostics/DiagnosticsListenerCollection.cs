using Xunit;

namespace Hardened.Requests.Runtime.Tests.Diagnostics;

/// <summary>
/// Serialises every test that registers an <c>ActivityListener</c> or a <c>MeterListener</c>.
/// </summary>
/// <remarks>
/// Both are process-global. A listener registered by a test running in parallel is visible to every
/// other test in the process, so an assertion that <em>nothing</em> is listening — which is the
/// property that lets the pipeline instrument unconditionally — cannot be made robust any other
/// way. Anything added for the rest of the telemetry work belongs in this collection too.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DiagnosticsListenerCollection {
    public const string Name = "diagnostics-listeners";
}
