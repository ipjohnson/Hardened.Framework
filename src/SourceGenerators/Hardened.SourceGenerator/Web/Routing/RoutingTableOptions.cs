using CSharpAuthor;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// What differs between the two callers of <see cref="RoutingTableGenerator"/> — the attribute-routed
/// table an application declares with <c>[Get]</c> and friends, and the table generated from a
/// description.
/// </summary>
/// <remarks>
/// <para>
/// Every member here is a value. There is deliberately no delegate, no strategy interface and no
/// virtual hook: the route walk itself has no legitimate variation, and the moment a caller can
/// substitute part of it the two tables can drift again. They already did once, for six months,
/// and it shipped a wrong status code.
/// </para>
/// <para>
/// <see cref="AdditionalRegistrations"/> is the one member that carries emitted code rather than a
/// setting, and it is still data — the caller builds the statements, this generator appends them.
/// It exists because the description-driven path registers JSON type-info resolvers, published spec
/// documents and interface-to-implementation mappings that depend on types internal to
/// <c>Hardened.Idl.SourceGenerator</c>. Sharing that code would mean making those types public to
/// move logic that was never part of the route tree.
/// </para>
/// </remarks>
public sealed class RoutingTableOptions {
    /// <summary>The attribute-routed defaults.</summary>
    public static readonly RoutingTableOptions Default = new();

    /// <summary>Name of the nested class the table is emitted into.</summary>
    public string ClassName { get; init; } = "RoutingTable";

    /// <summary>Suffix on the generated source hint, after the entry point's name.</summary>
    public string HintSuffix { get; init; } = ".Routing";

    /// <summary>Name of the static field that anchors the table's dependency registration.</summary>
    public string DependencyFieldName { get; init; } = "_routingTableDependencies";

    /// <summary>
    /// How type names are written, or null for the writer's own default.
    /// </summary>
    /// <remarks>
    /// The description-driven table qualifies globally; the attribute-routed one relies on the
    /// usings it emits. Null rather than a named default because the two are not two settings of
    /// one knob — one passes OutputContextOptions and the other passes nothing, and making the
    /// attribute-routed path construct an equivalent options object would change its output.
    /// </remarks>
    public TypeOutputMode? TypeOutputMode { get; init; }

    /// <summary>
    /// Marks the emitted table excluded from coverage. The description-driven table is generated
    /// from a document rather than written, so it is measured against a denominator nobody authored.
    /// </summary>
    public bool ExcludeFromCodeCoverage { get; init; }

    /// <summary>
    /// Whether to emit the OpenAPI document alongside the table. Only the attribute-routed path
    /// does: the description-driven path was generated from a document and does not re-derive one.
    /// </summary>
    public bool EmitOpenApiDocument { get; init; } = true;

    /// <summary>
    /// Whether the base path declared on the entry point applies. The description-driven path
    /// carries its prefix in the described routes themselves.
    /// </summary>
    public bool UseEntryPointBasePath { get; init; } = true;

    /// <summary>
    /// Statements appended to the generated dependency-registration method, already emitted by the
    /// caller. See the remarks on this type for why this is not a callback.
    /// </summary>
    public IReadOnlyList<IOutputComponent> AdditionalRegistrations { get; init; } =
        Array.Empty<IOutputComponent>();
}
