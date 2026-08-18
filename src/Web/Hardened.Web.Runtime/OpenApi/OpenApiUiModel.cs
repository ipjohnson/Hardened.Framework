namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// Everything the reference page needs, resolved from configuration before it is rendered.
/// </summary>
/// <remarks>
/// A model rather than the page reading configuration itself: an output is constructed by the
/// pipeline through <c>new()</c> and has nowhere to take a dependency, and keeping the resolution in
/// the handler is what makes the page a pure function of its model - which is the thing worth
/// testing.
/// </remarks>
public sealed record OpenApiUiModel(
    string Title,
    string DocumentPath,
    string ScriptUrl,
    string? ScriptIntegrity);
