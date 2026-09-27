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
}
