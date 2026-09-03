namespace Hardened.Requests.Runtime.Caching;

/// <summary>
/// A handler declared <c>[CacheResponse]</c> and the application registered no store.
/// </summary>
/// <remarks>
/// <para>
/// Raised rather than ignored. The alternative is an attribute that compiles, travels into the
/// handler's metadata and does nothing - which is the failure <c>[CacheControl]</c> spent three
/// years in, and the one a separate package exists to avoid.
/// </para>
/// <para>
/// <b>It is raised on a request to the handler, not at startup.</b> Handlers are constructed
/// lazily, on the first request their route matches, so there is no point at which the application
/// knows which of them declare this. Failing here is as early as the question can be asked.
/// </para>
/// <para>
/// <b>Recorded on the response rather than thrown.</b> The cache stage is ahead of the one that
/// turns a failure into bytes, and a filter on that side of the line refuses by recording and
/// continuing. Thrown, it unwound past the filter that would have written a body, so this message
/// reached the log and the caller got a 500 with nothing in it.
/// </para>
/// </remarks>
public class ResponseCacheStoreMissingException : InvalidOperationException {

    public ResponseCacheStoreMissingException(string handler)
        : base($"{handler} declares [CacheResponse] and no IResponseCacheStore is registered. " +
               "Reference Hardened.Requests.Caching.Memory and add [HardenedMemoryResponseCache] " +
               "to the application module, or register a store of your own.") {
        Handler = handler;
    }

    /// <summary>
    /// The handler that declared it, as "METHOD /path".
    /// </summary>
    public string Handler { get; }
}
