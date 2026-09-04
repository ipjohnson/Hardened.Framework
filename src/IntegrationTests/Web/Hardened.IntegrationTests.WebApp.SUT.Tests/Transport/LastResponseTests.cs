using System.Net;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// The response the pipeline answered last in this test, whichever door it went out of.
/// </summary>
/// <remarks>
/// This class and <see cref="LastResponseIsolationTests"/> run in parallel and read their own
/// answers, which is the confirmation the design asked for: the DependencyModules runner leaves
/// xUnit's <c>TestContext.Current</c> in place around the harness's startup and the test body.
/// </remarks>
public class LastResponseTests {

    [HardenedTest]
    public async Task AfterAClientCallItReportsWhatThePipelineAnswered(ProbeClient client) {
        using var response = await client.Pets(TestContext.Current.CancellationToken);

        Assert.Equal(401, LastResponse.Status);
        Assert.True(LastResponse.Headers.ContainsKey("WWW-Authenticate"));
        Assert.Equal((int)response.StatusCode, LastResponse.Status);
    }

    /// <summary>
    /// The harness asks for gzip on every request, so the pipeline answers it, and the body kept
    /// here is the one it wrote: the content coding is on the header and in the bytes, and undoing
    /// it is the reader's, as it is for a client.
    /// </summary>
    [HardenedTest]
    [Grants("pets:read")]
    public async Task AfterAHarnessCallItReportsTheSame(ITestWebApp app) {
        var response = await app.Get("/authorization/pets");

        Assert.Equal(200, LastResponse.Status);
        Assert.Equal(response.StatusCode, LastResponse.Status);
        Assert.StartsWith("application/json", LastResponse.ContentType);
        Assert.Equal("gzip", LastResponse.Headers["Content-Encoding"]);

        using var decoded = new GZipStream(new MemoryStream(LastResponse.Body), CompressionMode.Decompress);
        using var reader = new StreamReader(decoded, Encoding.UTF8);

        Assert.Contains("pets", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [HardenedTest]
    public async Task ACreatedStatusTheClientSwallowsIsStillReported(ITestWebApp app) {
        using var client = app.CreateHttpClient();
        using var response = await client.PostAsync("/verbs/created", new StringContent(""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(201, LastResponse.Status);
    }

    [HardenedTest]
    public async Task ItIsTheLastResponseNotTheFirst(ITestWebApp app) {
        await app.Get("/authorization/pets");
        await app.Get("/authorization/open");

        Assert.Equal(200, LastResponse.Status);
    }

    [HardenedTest]
    public void ReadingItBeforeAnyRequestFailsNamingTheTest() {
        var failure = Assert.Throws<InvalidOperationException>(() => LastResponse.Status);

        Assert.Contains(nameof(ReadingItBeforeAnyRequestFailsNamingTheTest), failure.Message);
        Assert.False(LastResponse.IsAvailable);
    }

    /// <summary>
    /// While the runner prepares a test - building its container and resolving its parameters -
    /// xUnit has the test method in scope and neither a test nor a test case yet. A response
    /// answered there is not kept, and the test body starts with nothing on record.
    /// </summary>
    [HardenedTest]
    public async Task WhileTheTestIsPreparedNothingIsKept([WhilePreparing] Preparation seen, ITestWebApp app) {
        Assert.True(seen.TestWasAbsent);
        Assert.True(seen.CaseWasAbsent);
        Assert.Equal(nameof(WhileTheTestIsPreparedNothingIsKept), seen.MethodName);
        Assert.Contains("there is no test running", seen.Message);
        Assert.False(seen.AvailableAfterARequest);

        Assert.False(LastResponse.IsAvailable);

        await app.Get("/authorization/open");

        Assert.True(LastResponse.IsAvailable);
    }

    public sealed record Preparation(bool TestWasAbsent, bool CaseWasAbsent, string MethodName, string Message, bool AvailableAfterARequest);

    /// <summary>Runs inside the runner's preparation of the test case, and reports what it saw.</summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    private sealed class WhilePreparingAttribute : Attribute, DependencyModules.Testing.Attributes.Interfaces.ITestParameterValueProvider {
        public void SetupServiceCollection(
            DependencyModules.Testing.Attributes.Interfaces.ITestMethodContext testMethod,
            Microsoft.Extensions.DependencyInjection.IServiceCollection serviceCollection,
            System.Reflection.ParameterInfo parameter) {
        }

        public async Task<object?> GetParameterValueAsync(
            DependencyModules.Testing.Attributes.Interfaces.ITestMethodContext testMethod,
            IServiceProvider serviceProvider,
            System.Reflection.ParameterInfo parameter) {
            var context = TestContext.Current;
            var message = Assert.Throws<InvalidOperationException>(() => LastResponse.Status).Message;

            var app = serviceProvider.GetRequiredService<ITestWebApp>();

            await app.Get("/authorization/open");

            return new Preparation(
                context.Test == null,
                context.TestCase == null,
                context.TestMethod?.MethodName ?? "",
                message,
                LastResponse.IsAvailable);
        }
    }
}

/// <summary>Reads its own answer while <see cref="LastResponseTests"/> reads its.</summary>
public class LastResponseIsolationTests {

    [HardenedTest]
    public async Task AParallelTestSeesOnlyItsOwnResponse(ITestWebApp app) {
        for (var round = 0; round < 20; round++) {
            var response = await app.Get("/authorization/open");

            Assert.Equal(200, response.StatusCode);
            Assert.Equal(200, LastResponse.Status);
            Assert.False(LastResponse.Headers.ContainsKey("WWW-Authenticate"));
        }
    }
}
