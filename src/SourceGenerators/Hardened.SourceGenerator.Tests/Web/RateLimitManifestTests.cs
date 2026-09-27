using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// The list of rate-limited handlers a routing table hands a host, which the Lambda runtime reads
/// to warn that its default store counts per execution environment.
/// </summary>
public class RateLimitManifestTests
{
    private static string Routing(string module, string controller) =>
        RequestGeneratorHarness
            .Generate(
                $$"""
                using Hardened.Requests.Runtime.RateLimiting;
                using Hardened.Shared.Runtime.Attributes;
                using Hardened.Web.Runtime.Attributes;

                namespace TestApp;

                [HardenedModule]
                {{module}}
                public partial class Application { }

                {{controller}}
                """
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

    private const string Quotes = """
        public class QuoteController {
            [Get("/quotes")]
            [RateLimit(PermitLimit = 60)]
            public string List() => "quotes";

            [Get("/quotes/{id}")]
            public string Read(string id) => id;
        }
        """;

    [Fact]
    public void ALimitedHandlerIsListedAndRegistered()
    {
        var routing = Routing("", Quotes);

        Assert.Contains("class RateLimits", routing);
        Assert.Contains("new string[] { \"GET /quotes\" }", routing);
        Assert.Matches(
            @"AddSingleton<\s*IRateLimitManifest,\s*Application\.RateLimits\s*>\(\)",
            routing
        );
    }

    /// <summary>
    /// An application with no limit generates what it generated before, which is also what keeps
    /// this off the checked-in routing fixtures.
    /// </summary>
    [Fact]
    public void NoLimitEmitsNothing()
    {
        var routing = Routing(
            "",
            """
            public class QuoteController {
                [Get("/quotes")]
                public string List() => "quotes";
            }
            """
        );

        Assert.DoesNotContain("RateLimits", routing);
        Assert.DoesNotContain("IRateLimitManifest", routing);
    }

    [Fact]
    public void ALimitOnTheClassListsEveryHandlerInIt()
    {
        var routing = Routing(
            "",
            """
            [RateLimit(PermitLimit = 60)]
            public class QuoteController {
                [Get("/quotes")]
                public string List() => "quotes";

                [Get("/quotes/{id}")]
                public string Read(string id) => id;
            }
            """
        );

        Assert.Contains("new string[] { \"GET /quotes\", \"GET /quotes/{id}\" }", routing);
    }

    /// <summary>A limit on the entry point covers every handler, so every handler is listed.</summary>
    [Fact]
    public void ALimitOnTheEntryPointListsEveryHandler()
    {
        var routing = Routing("[RateLimit(PermitLimit = 600)]", Quotes);

        Assert.Contains("new string[] { \"GET /quotes\", \"GET /quotes/{id}\" }", routing);
    }

    /// <summary>
    /// The template's layout: the module declares the prefix and the handler is its root. The
    /// warning names the route a request uses, which the handler's own template does not.
    /// </summary>
    [Fact]
    public void AHandlerUnderTheEntryPointsBasePathIsListedWithIt()
    {
        var routing = Routing(
            "[BasePath(\"/quotes\")]",
            """
            public class QuoteController {
                [Get("/")]
                [RateLimit(PermitLimit = 60)]
                public string List() => "quotes";

                [Get("/{id}")]
                [RateLimit(PermitLimit = 60)]
                public string Read(string id) => id;
            }
            """
        );

        Assert.Contains("new string[] { \"GET /quotes\", \"GET /quotes/{id}\" }", routing);
    }
}
