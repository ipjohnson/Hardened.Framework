using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Conditional;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// <c>[ConditionalGet]</c> declared on a class: one handler that knows its resource's version
/// and says so, and one that leaves the tag to the filter.
/// </summary>
/// <remarks>
/// The two sources of a validator on an uncached handler. <see cref="Read"/> writes
/// <c>ETag</c> and <c>Last-Modified</c> itself, so the filter passes its bytes straight through
/// and revalidates against what it wrote. <see cref="Generated"/> writes nothing, so the filter
/// holds its response back and tags the bytes as sent.
/// </remarks>
[BasePath("/conditional")]
[ConditionalGet]
public class ConditionalController {

    /// <summary>The version the document is at, as the entity-tag a client is given.</summary>
    public const string Version = "\"v7\"";

    /// <summary>When the document last changed, to the second, which is all the header carries.</summary>
    public static readonly DateTimeOffset UpdatedAt = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly HandlerCallCounter _counter;

    public ConditionalController(HandlerCallCounter counter) {
        _counter = counter;
    }

    [Get("/document")]
    public Document Read(IExecutionContext context) {
        var headers = context.Response.Headers;

        headers[KnownHeaders.ETag] = Version;
        headers[KnownHeaders.LastModified] = HttpDate.Format(UpdatedAt);

        return new Document { Version = Version, Served = _counter.Next("document") };
    }

    /// <summary>
    /// A body that is the same for the same query, so the tag the filter computes over it is
    /// stable per value and a different value is a different representation.
    /// </summary>
    [Get("/generated")]
    public string Generated([FromQueryString] string culture) => "generated-" + culture;

    public class Document {
        public string Version { get; set; } = "";

        /// <summary>Advances only when the handler runs, so a 304 that ran it is visible.</summary>
        public int Served { get; set; }
    }
}
