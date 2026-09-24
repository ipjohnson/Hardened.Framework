namespace Hardened.Web.Runtime.Cors;

/// <summary>
/// Registered by the generated routing table of a compilation in which a route or the entry point
/// declares <see cref="CorsAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// Its presence is what moves CORS from the whole application to the routes that declare it.
/// <c>CorsStartupService</c> reads it once, and installs a middleware filter that answers only
/// preflights.
/// </para>
/// <para>
/// Registered by the build rather than decided at startup, because a handler's declarations are
/// read as its filter chain is built on its first request. At startup there is no list of routes
/// to read them from.
/// </para>
/// </remarks>
public sealed class CorsManifest { }
