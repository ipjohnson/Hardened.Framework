namespace Hardened.Requests.Abstract.Caching;

/// <summary>
/// A handler's declaration that its responses may be stored, without naming the strategy's type.
/// </summary>
/// <remarks>
/// <para>
/// <c>CacheResponseAttribute&lt;VaryByQuery&gt;</c> and
/// <c>CacheResponseAttribute&lt;VaryByHeader&gt;</c> are different types, so anything asking "does
/// this handler already declare caching" cannot ask by exact type. This is what it asks by.
/// </para>
/// <para>
/// It is also how composition works. <c>[CacheResponse&lt;T&gt;]</c> is
/// <c>AllowMultiple = true</c>, and each attribute only ever sees itself - but every one of them
/// is in <c>IExecutionRequestHandlerInfo.Metadata</c>, so the first can read the rest through this
/// interface and build one filter over the composite key rather than each contributing a filter of
/// its own.
/// </para>
/// </remarks>
public interface ICacheResponseDeclaration {

    /// <summary>
    /// How long a stored response stays valid, in seconds, or 0 for the default.
    /// </summary>
    int Duration { get; }

    /// <summary>
    /// Who a stored response may be served to.
    /// </summary>
    /// <remarks>
    /// Defaulted so that a declaration written before this existed still compiles. It means
    /// <see cref="CacheScope.Unstated"/>, which is a failure naming the handler on anything that
    /// requires something of its caller rather than a quiet reading of it.
    /// </remarks>
    CacheScope Scope => CacheScope.Unstated;

    /// <summary>
    /// The strategy this declaration names, built from the values it carries.
    /// </summary>
    /// <remarks>
    /// Non-generic, because the declaration reading this one cannot name its type argument. The
    /// generic attribute answers with <c>TProvider.Create(Values)</c>, so the constraint is still
    /// what makes the call legal.
    /// </remarks>
    ICacheKeyProvider CreateKeyProvider();
}
