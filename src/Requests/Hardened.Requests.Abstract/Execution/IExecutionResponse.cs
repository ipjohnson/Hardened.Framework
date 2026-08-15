using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Templates;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Execution;

public interface IExecutionResponse {
    IExecutionResponse Clone(IHeaderCollection? headerCollection = null);

    string? ContentType { get; set; }

    object? ResponseValue { get; set; }

    /// <summary>
    /// Builds the view this response renders through, or null when it renders through none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Assigned by the generated handler from <c>[Template&lt;T&gt;]</c> before the handler runs, so
    /// a handler or filter can replace it - a different view for mobile than for desktop, an A/B
    /// test, an error view. That is the same dynamic selection a template name allowed, and it is
    /// typed now.
    /// </para>
    /// <para>
    /// A factory rather than an instance so nothing is allocated for a response that is never
    /// rendered - an exception path, a short-circuiting filter, a 304.
    /// </para>
    /// </remarks>
    Func<IExecutionContext, IHardenedTemplate>? TemplateFactory { get; set; }

    /// <summary>
    /// The view once built, or null before anything has needed it.
    /// </summary>
    /// <remarks>
    /// Content negotiation asks a template what it produces, and does so once per media type the
    /// client listed, so the instance is built on first use and kept here rather than rebuilt per
    /// question. A filter may also read or replace it after selection has run.
    /// </remarks>
    IHardenedTemplate? Template { get; set; }

    int? Status { get; set; }

    bool ShouldCompress { get; set; }

    Stream Body { get; set; }

    IDictionary<string, StringValues> Headers { get; }

    Exception? ExceptionValue { get; set; }

    bool ResponseStarted { get; }

    bool IsBinary { get; set; }

    ICookieSetCollection Cookies { get; }

    bool ShouldSerialize { get; set; }
}