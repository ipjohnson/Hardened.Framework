using Hardened.Aws.Lambda.Http;
using Hardened.Aws.Lambda.Testing;

namespace Hardened1.Tests;

/// <summary>
/// The same requests TodoTests sends, built as API Gateway payload format 2.0 events and sent
/// through the function's invocation handler, so the Lambda HTTP adapter is exercised without a
/// Lambda runtime.
/// </summary>
/// <remarks>
/// [LambdaHttpModule] loads the adapter, which the library module under test does not import.
/// Without it every request fails with "No service for type LambdaInvocationHandler".
/// </remarks>
[LambdaWebTesting]
[LambdaHttpModule]
public class TodoLambdaTests
{
    [ModuleTest]
    public async Task ListTodos_ThroughTheInvocationHandler(ITestWebApp app)
    {
        var response = await app.Get("/todos");

        response.Assert.Ok();

        // Only a server writes Date, so its absence shows no socket answered the request.
#if (xunit)
        Assert.False(response.Headers.ContainsKey("Date"));
#else
        Assert.That(response.Headers.ContainsKey("Date"), Is.False);
#endif
    }

    [ModuleTest]
    public async Task APathWithNoRoute_Is404(ITestWebApp app)
    {
#if (xunit)
        Assert.Equal(404, (await app.Get("/nothing-here")).StatusCode);
#else
        Assert.That((await app.Get("/nothing-here")).StatusCode, Is.EqualTo(404));
#endif
    }
}
