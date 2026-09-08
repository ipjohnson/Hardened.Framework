using DotNet.Testcontainers.Containers;

namespace Hardened.Functions.Testing.Containers;

/// <summary>
/// What the application in a container has reported so far, read from its output.
/// </summary>
/// <remarks>
/// <para>
/// Polled rather than streamed. Docker's log endpoint returns everything the container has
/// written, so asking again a quarter of a second later costs one request and needs no background
/// reader whose failure a test would have to notice. Every trigger this ladder covers delivers in
/// seconds, so the poll interval is not what a test waits on.
/// </para>
/// <para>
/// A wait that times out fails with every line the container printed, because the usual reason is
/// not that the handler was slow but that the message never arrived, and the host's own log is
/// where it says why.
/// </para>
/// </remarks>
public sealed class ObservedInvocations {
    private readonly IContainer _container;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public ObservedInvocations(IContainer container) {
        _container = container;
    }

    /// <summary>Everything observed so far.</summary>
    public async Task<IReadOnlyList<Observation>> Current(CancellationToken cancellationToken = default) {
        var (stdout, stderr) = await _container.GetLogsAsync(ct: cancellationToken);

        // Both streams. A console logger writes to stdout and a host may redirect the worker's
        // output to stderr, and which one a marker lands on is not the application's decision.
        return Observation.Parse(stdout + "\n" + stderr);
    }

    /// <summary>
    /// The first observation matching <paramref name="predicate"/>, waiting for it to appear.
    /// </summary>
    public async Task<Observation> WaitFor(
        Func<Observation, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) {
        var observations = await WaitUntil(
            all => all.Any(predicate), timeout, cancellationToken, "an observation matching the predicate");

        return observations.First(predicate);
    }

    /// <summary>
    /// Every observation once at least <paramref name="count"/> have appeared.
    /// </summary>
    public Task<IReadOnlyList<Observation>> WaitForCount(
        int count, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        WaitUntil(all => all.Count >= count, timeout, cancellationToken, $"{count} observation(s)");

    private async Task<IReadOnlyList<Observation>> WaitUntil(
        Func<IReadOnlyList<Observation>, bool> condition,
        TimeSpan? timeout,
        CancellationToken cancellationToken,
        string expectation) {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (true) {
            var observations = await Current(cancellationToken);

            if (condition(observations)) {
                return observations;
            }

            if (DateTime.UtcNow > deadline) {
                var (stdout, stderr) = await _container.GetLogsAsync(ct: cancellationToken);

                throw new TimeoutException(
                    $"Waited {(timeout ?? DefaultTimeout).TotalSeconds:0} s for {expectation} and saw " +
                    $"{observations.Count}. The container printed:\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
