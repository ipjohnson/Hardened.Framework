using System.Diagnostics;
using Xunit;

namespace Hardened.Amz.DynamoDbClient.Tests;

/// <summary>
/// A fact that needs a Docker daemon, skipped rather than failed when there is not one.
///
/// <para>
/// This is the first test in the repository with an external requirement, so it sets the shape:
/// a machine without Docker reports a skip that says why, rather than a container failure that has
/// to be read to find out it was never about the code. CI has a daemon, so these run there.
/// </para>
/// </summary>
public sealed class RequiresDockerFactAttribute : FactAttribute {
    public RequiresDockerFactAttribute() {
        if (!DockerDaemon.IsAvailable) {
            Skip = "No Docker daemon, so DynamoDB Local cannot start.";
        }
    }
}

internal static class DockerDaemon {
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
