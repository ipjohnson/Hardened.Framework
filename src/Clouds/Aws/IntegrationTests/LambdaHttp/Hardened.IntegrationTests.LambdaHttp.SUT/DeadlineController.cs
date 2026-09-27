using Hardened.Requests.Runtime.Filters;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.LambdaHttp.SUT;

/// <summary>
/// A handler that says whether its token was cancelled before a delay longer than any test waits.
/// </summary>
public class DeadlineController
{
    [Get("/deadline")]
    public async Task<string> Wait(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

            return "finished";
        }
        catch (OperationCanceledException)
        {
            return "cancelled";
        }
    }

    /// <summary>
    /// A budget longer than the invocation has left, on a handler that lets the cancellation
    /// escape, as a handler that passes its token on does.
    /// </summary>
    [Get("/deadline/bounded")]
    [Timeout(Milliseconds = 60_000)]
    public async Task<string> Bounded(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

        return "finished";
    }
}
