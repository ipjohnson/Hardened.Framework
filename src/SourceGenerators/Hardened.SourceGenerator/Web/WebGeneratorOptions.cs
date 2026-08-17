namespace Hardened.SourceGenerator.Web;

/// <summary>
/// The MSBuild properties the web generator reads, as one cacheable value.
/// </summary>
/// <remarks>
/// <para>
/// Raw strings rather than parsed values, and that is deliberate: an incremental generator compares
/// this to decide whether to re-run, and the comparison has to be over what the build said. Parsing
/// happens where a diagnostic can be reported, which a provider's <c>Select</c> cannot do.
/// </para>
/// <para>
/// A record for the equality. Two instances built from the same build properties compare equal, so
/// an edit to a handler does not invalidate the routing table through this input.
/// </para>
/// </remarks>
/// <param name="AmbiguousRoutes">
/// <c>&lt;HardenedAmbiguousRoutes&gt;</c> - the default severity for <c>HRDR001</c>, layered on top
/// of the per-file <c>.editorconfig</c> mechanism.
/// </param>
/// <param name="OpenApiVersion">
/// <c>&lt;HardenedOpenApiVersion&gt;</c> - which version the emitted document declares, and which
/// spellings it uses. Null takes the default.
/// </param>
public record WebGeneratorOptions(string? AmbiguousRoutes, string? OpenApiVersion) {

    /// <summary>What a build that set nothing gets.</summary>
    public static readonly WebGeneratorOptions Default = new(null, null);
}
