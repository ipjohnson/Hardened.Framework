using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// The list of event-stream handlers a routing table hands a host, which the Lambda runtime reads
/// to warn when its response mode buffers them.
/// </summary>
public class ServerSentEventManifestTests
{
    [Fact]
    public void AHandlerUnderTheEntryPointsBasePathIsListedWithIt()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                """
                using System.Collections.Generic;
                using System.Threading.Tasks;
                using Hardened.Shared.Runtime.Attributes;
                using Hardened.Web.Runtime.Attributes;

                namespace TestApp;

                [HardenedModule]
                [BasePath("/v1")]
                public partial class Application { }

                public class FeedController {
                    [Get("/feed")]
                    [ServerSentEvents]
                    public async IAsyncEnumerable<string> Feed() {
                        yield return "one";
                        await Task.CompletedTask;
                    }
                }
                """
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains("new string[] { \"GET /v1/feed\" }", routing);
    }
}
