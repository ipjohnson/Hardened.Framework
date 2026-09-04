using Hardened.IntegrationTests.WebApp.SUT.Services;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// A handler that knows its resource's version and says so.
/// </summary>
/// <remarks>
/// The other source of a validator. The response cache tags what it stores, and static content
/// hashes what it serves; an uncached handler gets nothing computed for it and writes
/// <c>ETag</c> or <c>Last-Modified</c> itself when it has one to write. The filter that answers a
/// 304 is installed on every GET by the web module, so this declares nothing else.
/// </remarks>
[BasePath("/conditional")]
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

    public class Document {
        public string Version { get; set; } = "";

        /// <summary>Advances only when the handler runs, so a 304 that ran it is visible.</summary>
        public int Served { get; set; }
    }
}
