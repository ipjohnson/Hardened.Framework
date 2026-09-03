using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Caching;

namespace Hardened.Requests.Runtime.Caching;

/// <summary>
/// A handler requires something of its caller, declares <c>[CacheResponse]</c>, and did not say who
/// a stored response may be served to.
/// </summary>
/// <remarks>
/// <para>
/// Raised rather than read either way, because both readings are behaviour somebody would call a
/// defect. Sharing one entry among callers is a data leak the moment the handler's answer depends
/// on who asked; keying per caller is safe and silently turns a cache that was shedding load into
/// one entry per caller. See <see cref="CacheScope"/>.
/// </para>
/// <para>
/// Raised as the handler's filter chain is built, alongside the other two failures a declaration
/// can express, so it names the handler and is asked once rather than per request.
/// </para>
/// </remarks>
public class CacheScopeUndeclaredException : InvalidOperationException {

    public CacheScopeUndeclaredException(string handler, Requirement requirement)
        : base($"{handler} requires {requirement} of its caller and declares [CacheResponse] " +
               "without saying who a stored response may be served to. Set " +
               "Scope = CacheScope.PerCaller if the answer depends on who asked - an owner-scoped " +
               "read, anything filtered by the caller's tenant - or Scope = CacheScope.AllCallers " +
               "if every caller the guard admits gets the same bytes.") {
        Handler = handler;
    }

    /// <summary>
    /// The handler that declared it, as "METHOD /path".
    /// </summary>
    public string Handler { get; }
}
