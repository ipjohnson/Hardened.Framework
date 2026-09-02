namespace Hardened.Requests.Runtime.Caching;

/// <summary>
/// A handler declared <c>[CacheResponse]</c> and the application registered no store.
/// </summary>
/// <remarks>
/// <para>
/// Thrown rather than ignored. The alternative is an attribute that compiles, travels into the
/// handler's metadata and does nothing - which is the failure <c>[CacheControl]</c> spent three
/// years in, and the one a separate package exists to avoid.
/// </para>
/// <para>
/// <b>It is raised on the first request to the handler, not at startup.</b> Handlers are
/// constructed lazily, on the first request their route matches, so there is no point at which the
/// application knows which of them declare this. Failing here is as early as the question can be
/// asked.
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
