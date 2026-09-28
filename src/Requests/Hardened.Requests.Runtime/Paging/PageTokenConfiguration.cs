using Hardened.Shared.Runtime.Application;

namespace Hardened.Requests.Runtime.Paging;

/// <summary>
/// The key <c>IPageTokens</c> signs tokens with. See <see cref="PageTokenConfiguration"/>.
/// </summary>
public interface IPageTokenConfiguration
{
    string? Key { get; }
}

/// <summary>
/// Read once at startup from <see cref="EnvironmentVariable"/>, and open to amendment through the
/// application's configuration:
/// </summary>
/// <code>
/// config.Amend((PageTokenConfiguration tokens) => tokens.Key = secrets.PageTokenKey);
/// </code>
/// <remarks>
/// <para>
/// The key's UTF-8 bytes are the HMAC key, so any string works. Every instance that serves the
/// same pages needs the same key, or a token one instance wrote is refused by the next.
/// </para>
/// <para>
/// Null or empty signs nothing. Changing the key refuses every token written under the old one,
/// and a client holding one starts again from the first page.
/// </para>
/// </remarks>
public class PageTokenConfiguration : IPageTokenConfiguration
{
    public const string EnvironmentVariable = "HARDENED_PAGE_TOKEN_KEY";

    public string? Key { get; set; }

    public static void FromEnvironment(
        IHardenedEnvironment environment,
        PageTokenConfiguration configuration
    )
    {
        configuration.Key = environment.Value<string>(EnvironmentVariable);
    }
}
