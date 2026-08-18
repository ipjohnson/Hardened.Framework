namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// What the reference page is built from.
/// </summary>
public interface IOpenApiUiConfiguration {
    /// <summary>The page title.</summary>
    string Title { get; }

    /// <summary>Where the page fetches the document from.</summary>
    string DocumentPath { get; }

    /// <summary>Where the browser loads the reference UI from.</summary>
    string ScriptUrl { get; }

    /// <summary>
    /// The subresource integrity hash for <see cref="ScriptUrl"/>, or null when there is none to
    /// state.
    /// </summary>
    string? ScriptIntegrity { get; }
}

/// <inheritdoc />
public sealed class OpenApiUiConfiguration : IOpenApiUiConfiguration {
    public OpenApiUiConfiguration(
        string title, string documentPath, string scriptUrl, string? scriptIntegrity) {
        Title = title;
        DocumentPath = documentPath;
        ScriptUrl = scriptUrl;
        ScriptIntegrity = scriptIntegrity;
    }

    public string Title { get; }

    public string DocumentPath { get; }

    public string ScriptUrl { get; }

    public string? ScriptIntegrity { get; }
}
