namespace Hardened.Functions.Testing.Containers;

/// <summary>
/// How an application inside a container tells a test what its handlers did.
/// </summary>
/// <remarks>
/// <para>
/// A handler running in another process cannot be observed through a <c>[Mock]</c>. What it can do
/// is print, and the container's output is something Docker hands back on request. So a fixture's
/// collaborator prints one line per invocation: this prefix, then a single-line JSON object saying
/// what happened. The same trick the AOT probe in the CI workflow uses with <c>HANDLED</c>, made
/// structured so a test can assert on a field rather than on a substring.
/// </para>
/// <para>
/// The application writes the literal string rather than referencing this constant, on purpose:
/// this project depends on Testcontainers, and a published function must not.
/// </para>
/// </remarks>
public static class ObservationMarker {
    public const string Prefix = "HARDENED-OBSERVED ";
}
