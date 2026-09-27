using DependencyModules.xUnit.Attributes;
using Hardened.Aws.Lambda.Testing;
using Hardened.Web.Testing;
using Xunit;

namespace Hardened.IntegrationTests.LambdaHttp.SUT.Tests;

/// <summary>
/// The invocation's remaining time, which the runtime turns into the handler's token.
/// </summary>
public class DeadlineTests
{
    /// <summary>
    /// 700 milliseconds remaining leaves 200 before the runtime's 500 millisecond margin, so the
    /// handler's token is cancelled long before its 10 second delay ends.
    /// </summary>
    [ModuleTest]
    [LambdaWebTesting(RemainingTimeMilliseconds = 700)]
    public async Task AShortRemainingTimeCancelsTheHandlersToken(ITestWebApp app)
    {
        var response = await app.Get("/deadline");

        Assert.Equal("cancelled", response.Deserialize<string>());
    }
}
