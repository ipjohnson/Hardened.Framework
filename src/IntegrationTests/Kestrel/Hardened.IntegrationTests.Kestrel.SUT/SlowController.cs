using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Kestrel.SUT;

/// <summary>
/// A request that is still running when the process is told to stop.
/// </summary>
public class SlowController
{
    /// <summary>
    /// Answers three seconds after it starts. The line it prints first tells a harness that the
    /// request is in flight, so a signal sent after it cannot arrive before the request did.
    /// </summary>
    [Get("/slow")]
    public async Task<EchoResult> Slow()
    {
        Console.WriteLine("IN FLIGHT");

        await Task.Delay(TimeSpan.FromSeconds(3));

        return new EchoResult { Method = "GET" };
    }
}
