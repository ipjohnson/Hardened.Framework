using Hardened.IntegrationTests.WebApp.SUT.Controllers;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Headers;
using Hardened.Web.Testing;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// <c>[Timeout]</c> through the real pipeline: the token the handler was actually handed, the
/// status the converter turned its cancellation into, and the operations that declared nothing.
/// </summary>
/// <remarks>
/// A built application rather than a filter driven by hand, because the two things that can be
/// wrong here are invisible to a filter test. The handler's <c>CancellationToken</c> parameter is
/// bound from the context at <c>FilterOrder.Serialization</c>, so a filter one stage later would
/// pass every unit test and bound nothing; and the status is written by
/// <c>ExceptionToModelConverter</c> during that same serialization, inside the filter's own span.
/// </remarks>
public class TimeoutTests {

    [HardenedTest]
    public async Task AHandlerThatOutlivesItsBudgetIs504(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/timeout/slow");

        Assert.Equal(504, response.StatusCode);
        Assert.Equal("GatewayTimeout", response.Deserialize<ErrorModel>().Type);
    }

    /// <summary>
    /// The deadline is a bound rather than a delay: an operation that finishes inside it answers
    /// normally, and the handler ran once.
    /// </summary>
    [HardenedTest]
    public async Task AHandlerThatFinishesInsideItsBudgetAnswersNormally(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/timeout/fast");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("fast-1", response.Deserialize<string>());
    }

    /// <summary>
    /// An operation shedding load says so, with the window only it knows. This is the one spelling
    /// that can: the application-wide default has no handler metadata for the converter to read
    /// and always answers 504.
    /// </summary>
    [HardenedTest]
    public async Task ADeclaredStatusAndRetryAfterReachTheCaller(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/timeout/shed");

        Assert.Equal(503, response.StatusCode);
        Assert.Equal("ServiceUnavailable", response.Deserialize<ErrorModel>().Type);
        Assert.Equal("30", response.Headers[KnownHeaders.RetryAfter].ToString());
    }

    /// <summary>A 504 knows nothing about when the dependency recovers, so it sends no number.</summary>
    [HardenedTest]
    public async Task A504SendsNoRetryAfter(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/timeout/slow");

        Assert.False(response.Headers.ContainsKey(KnownHeaders.RetryAfter));
    }

    /// <summary>
    /// An operation that declares nothing still answers normally: the cascade found this
    /// assembly's declaration, which is a bound rather than a delay.
    /// </summary>
    [HardenedTest]
    public async Task AnOperationThatDeclaresNothingStillAnswers(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/timeout/unbounded");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("unbounded-1", response.Deserialize<string>());
    }

    // -------------------------------------------------------------------- the cascade

    /// <summary>
    /// The assembly rung. Nothing on this operation or its class declares a deadline, so the
    /// <c>[assembly: Timeout]</c> beside these controllers is what bounds it.
    /// </summary>
    /// <remarks>
    /// Read back off the handler rather than waited for, which is what making the budget
    /// first-class buys: the filter enforcing it and the handler reporting it are the same value.
    /// </remarks>
    [HardenedTest]
    public async Task AnOperationDeclaringNothingInheritsItsAssembly(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/timeout/budget");

        Assert.Equal(300_000, response.Deserialize<int>());
    }

    /// <summary>A class-level declaration is nearer than the assembly, so it wins.</summary>
    [HardenedTest]
    public async Task AClassBeatsItsAssembly(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/timeout/classed/budget");

        Assert.Equal(20_000, response.Deserialize<int>());
    }

    /// <summary>
    /// A method beats its class, upwards. This is the case a tightest-wins rule cannot express, and
    /// the reason resolution takes the first declaration in metadata rather than the smallest.
    /// </summary>
    [HardenedTest]
    public async Task AMethodBeatsItsClassEvenWhenItLoosens(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/timeout/classed/slower");

        Assert.Equal(40_000, response.Deserialize<int>());
    }

    /// <summary>An operation's own declaration is the nearest rung of all.</summary>
    [HardenedTest]
    public async Task AnOperationBeatsEverythingAboveIt(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/timeout/slow");

        Assert.Equal(504, response.StatusCode);
    }

    /// <summary>
    /// One budget covers every attempt rather than resetting per attempt. The filter sits ahead of
    /// <c>FilterOrder.Retry</c>, so the retry loop runs inside the deadline and stops when it
    /// fires; five attempts at forty milliseconds of backoff cannot fit in a hundred and fifty.
    /// </summary>
    [HardenedTest]
    public async Task OneBudgetCoversEveryRetryAttempt(ITestWebApp testWebApp) {
        await testWebApp.Get("/timeout/retried");

        var attempts = (await testWebApp.Get("/timeout/calls/retried")).Deserialize<int>();

        Assert.InRange(attempts, 1, 4);
    }

}
