using Hardened.Idl.BuildTask;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;

namespace Hardened.OpenApi.BuildTask;

/// <summary>
/// Reads each OpenAPI document, emits everything that is a pure spec-to-C# transformation, and
/// writes a normalised model for the source generator to pick up as an <c>AdditionalFile</c>.
/// </summary>
/// <remarks>
/// <para>
/// The spec is parsed once, here, and never opened again. That is what lets
/// <c>Hardened.OpenApi.SourceGenerator</c> ship without Microsoft.OpenApi and its YAML reader
/// embedded as resources, without the <c>AssemblyResolve</c> hook that loaded them, and without the
/// RS1035 suppression that hook required.
/// </para>
/// <para>
/// Records, enums, service interfaces, the JSON type info resolver and the filter attributes are all
/// written from here: none of them needs to look at the compilation, so none of them needs a Roslyn
/// generator. What stays in the generator is handler classes, which are matched against
/// <c>[Handler]</c> declarations, and the routing table, which is anchored on the entry point.
/// </para>
/// <para>
/// Everything above that is not the reading itself lives in <see cref="ExtractSpecTask"/>, which
/// this shares with any other description language. What remains here is the OpenAPI document: one
/// <see cref="Parse"/> override, three properties that only an OpenAPI document has, and the strings
/// that name this front end in diagnostics.
/// </para>
/// </remarks>
public sealed class ExtractOpenApiSpec : ExtractSpecTask {

    /// <summary>
    /// Whether operations with no tag are grouped by first path segment.
    /// </summary>
    /// <remarks>
    /// Off, so an untagged document produces one service, which is what most of them mean. Turn it
    /// on for a description that carries no tags at all and hundreds of operations - DigitalOcean's
    /// is one - where a single interface with every method on it is not what anyone implements.
    /// </remarks>
    public bool GroupUntaggedByPath { get; set; }

    /// <summary>
    /// Whether <c>$ref</c>s into other files are followed.
    /// </summary>
    /// <remarks>
    /// Off, and deliberately. A reference may name any URL, so following them turns a build into
    /// something that reaches the network - not reproducible, and not safe against a description
    /// someone else publishes. On, resolution is rooted at the directory holding the specification.
    /// </remarks>
    public bool LoadExternalRefs { get; set; }

    /// <summary>
    /// Whether the path of the first <c>servers</c> entry prefixes every route.
    /// </summary>
    /// <remarks>
    /// Off unless asked for. See <c>OpenApiSpecParser.ServerBasePath</c> for why applying it
    /// unasked is the wrong default.
    /// </remarks>
    public bool ApplyServerBasePath { get; set; }

    protected override string DiagnosticPrefix => "HOAT";

    protected override string SpecNoun => "OpenAPI spec";

    protected override string EmitUnreferencedSchemasProperty =>
        "HardenedOpenApiEmitUnreferencedSchemas";

    /// <summary>
    /// Turns one OpenAPI document into the neutral model everything downstream works on.
    /// </summary>
    /// <remarks>
    /// The only code in this task that knows what an OpenAPI document is. Everything past it - the
    /// emitters, the validation, the diagnostics, the source generator that reads the written model
    /// - operates on <see cref="ServiceSpecModel"/> and contains no OpenAPI concept.
    /// </remarks>
    internal override ServiceSpecModel? Parse(
        string document, string fileName, string specPath, ICollection<string> diagnostics) =>
        OpenApiSpecParser.Parse(
            document, fileName, CancellationToken.None, ApplyServerBasePath, diagnostics,
            GroupUntaggedByPath,
            LoadExternalRefs ? Path.GetDirectoryName(Path.GetFullPath(specPath)) : null);
}
