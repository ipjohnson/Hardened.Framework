using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Links;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Links;

/// <summary>
/// What a host tells the link builders about where it is served from.
/// </summary>
public interface ILinkConfiguration {
    string BasePath { get; }

    string? Scheme { get; }

    string? Host { get; }
}

/// <inheritdoc cref="ILinkConfiguration" />
public class LinkConfiguration : ILinkConfiguration {
    /// <summary>
    /// Prefixed to every generated link. Empty by default, which is right for a host that serves
    /// the application at the root - Kestrel and ASP.NET Core do.
    /// </summary>
    /// <remarks>
    /// A host that strips a prefix before the application sees the path has to put it back here, or
    /// every link it generates is missing that prefix. API Gateway's stage is the case this exists
    /// for.
    /// </remarks>
    public string BasePath { get; set; } = "";

    public string? Scheme { get; set; }

    public string? Host { get; set; }
}

/// <summary>
/// The default <see cref="ILinkContext"/>, from configuration.
/// </summary>
/// <remarks>
/// <c>Try</c>, so a host that knows better - one that can read the stage or the forwarded host off
/// the request it is handling - replaces it by registering its own first.
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class LinkContext : ILinkContext {
    public LinkContext(IOptions<ILinkConfiguration> configuration) {
        var value = configuration.Value;

        // Trimmed, so a base path configured either way composes with a route that always starts
        // with one. "/prod" and "/prod/" have to produce the same link.
        BasePath = value.BasePath.TrimEnd('/');
        Scheme = value.Scheme;
        Host = value.Host;
    }

    public string BasePath { get; }

    public string? Scheme { get; }

    public string? Host { get; }

    public string Resolve(string path) => BasePath.Length == 0 ? path : BasePath + path;

    public string Absolute(string path) {
        var resolved = Resolve(path);

        return string.IsNullOrEmpty(Scheme) || string.IsNullOrEmpty(Host)
            ? resolved
            : Scheme + "://" + Host + resolved;
    }
}
