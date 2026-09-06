using System.Diagnostics;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.v3;

namespace Hardened.Amz.DynamoDbClient.Tests;

/// <summary>
/// A fact that needs a Docker daemon. It carries the <c>Category=RequiresDocker</c> trait, so a
/// machine without one excludes it by saying so:
/// <code>dotnet test --filter "Category!=RequiresDocker"</code>
///
/// <para>
/// It does not skip. <c>testing-conventions.md</c> §10 is explicit that a Docker test fails rather
/// than skips on a machine without a daemon, and §3.3 of <c>TESTING-PLAN.md</c> fails the build on
/// any skipped test at all — so a skip would not have been the quiet outcome it looks like. The
/// trait is what makes the exclusion a decision the runner records rather than one the test makes
/// on its own, silently, at the moment it would otherwise have told you something.
/// </para>
///
/// <para>
/// This is the repository's one pattern for an external requirement. A second test needing Docker
/// uses this attribute rather than inventing its own detection.
/// </para>
/// </summary>
public sealed class RequiresDockerFactAttribute(
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1)
    : FactAttribute(sourceFilePath, sourceLineNumber), ITraitAttribute {

    /// <summary>The trait every Docker-dependent test in this repository carries.</summary>
    public const string Category = "RequiresDocker";

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new KeyValuePair<string, string>("Category", Category)];
}

/// <summary>
/// Whether a Docker daemon is reachable. Public so a fixture can say why a container did not start,
/// rather than leaving a connection failure to be read as a defect in the code under test.
/// </summary>
public static class DockerDaemon {
    private static readonly Lazy<bool> Available =
        new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    public static bool IsAvailable => Available.Value;

    private static bool Detect() {
        try {
            // `docker info` talks to the daemon, unlike `docker version`, which answers from the
            // client alone and so reports success with nothing running.
            using var probe = Process.Start(new ProcessStartInfo("docker", "info") {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (probe is null) {
                return false;
            }

            if (!probe.WaitForExit(milliseconds: 15_000)) {
                probe.Kill(entireProcessTree: true);

                return false;
            }

            return probe.ExitCode == 0;
        }
        catch (Exception) {
            // Docker is not on the path, or cannot be run. Either way there is no daemon to use.
            return false;
        }
    }
}
