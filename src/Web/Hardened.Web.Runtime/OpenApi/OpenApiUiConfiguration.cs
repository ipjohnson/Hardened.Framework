namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// One reference page: where it is served, and what it renders.
/// </summary>
/// <remarks>
/// Not a registered service. An application may serve several pages - one per specification it
/// publishes - so a single <c>IOpenApiUiConfiguration</c> in the container would be whichever module
/// registered last. Each provider holds its own instead.
/// </remarks>
public interface IOpenApiUiConfiguration {
    /// <summary>Where the page itself is served.</summary>
    string Path { get; }

    /// <summary>The page title.</summary>
    string Title { get; }

    /// <summary>Where the page fetches the document from.</summary>
    string DocumentPath { get; }

    /// <summary>Where the browser loads the reference UI from.</summary>
    string ScriptUrl { get; }

    /// <summary>
    /// The subresource integrity hash for <see cref="ScriptUrl"/>, or null or empty when there is
    /// none to state.
    /// </summary>
    string? ScriptIntegrity { get; }
}

/// <inheritdoc />
public sealed class OpenApiUiConfiguration : IOpenApiUiConfiguration {
    public OpenApiUiConfiguration(
        string path, string title, string documentPath, string scriptUrl, string? scriptIntegrity) {
        Path = path;
        Title = title;
        DocumentPath = documentPath;
        ScriptUrl = scriptUrl;
        ScriptIntegrity = scriptIntegrity;
    }

    public string Path { get; }

    public string Title { get; }

    public string DocumentPath { get; }

    public string ScriptUrl { get; }

    public string? ScriptIntegrity { get; }
}
