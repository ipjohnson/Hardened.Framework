using Hardened.Idl.BuildTask;
using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;

namespace Hardened.Smithy.BuildTask;

/// <summary>
/// Reads each Smithy JSON AST and produces the same normalised model the OpenAPI task produces.
/// </summary>
/// <remarks>
/// <para>
/// <b>This task ingests an AST, never a <c>.smithy</c> file.</b> That is the whole distribution
/// decision, and making it here is what keeps it from having to be right up front: given a
/// <c>.json</c> on disk, nothing is resolved, nothing is downloaded, and no tool is executed, so a
/// build stays hermetic and offline. Running <c>smithy ast --flatten model/ &gt; model.json</c> and
/// committing the result is therefore a first-class way to use this, not a workaround. A separate
/// optional target may run the CLI for you; this task neither knows nor cares whether it did.
/// </para>
/// <para>
/// Everything past <see cref="Parse"/> is <see cref="ExtractSpecTask"/> - the same slicing,
/// diagnostics, emitting and writing the OpenAPI front end gets, over the same
/// <see cref="ServiceSpecModel"/>. The model file is written with the same suffix too, which is what
/// lets the existing source generator pick it up with no change: the file holds an IR, not a
/// description.
/// </para>
/// </remarks>
public sealed class ExtractSmithySpec : ExtractSpecTask {

    /// <summary>
    /// The service shape to generate, or empty for every service the model declares.
    /// </summary>
    /// <remarks>
    /// A Smithy model may declare several services, which is more explicit than OpenAPI's tags and
    /// means the choice has to be expressible rather than inferred. An absolute shape id -
    /// <c>com.example#PetStore</c>.
    /// </remarks>
    public string ServiceShapeId { get; set; } = "";

    protected override string DiagnosticPrefix => "HSMT";

    protected override string SpecNoun => "Smithy model";

    protected override string EmitUnreferencedSchemasProperty =>
        "HardenedSmithyEmitUnreferencedSchemas";

    /// <summary>
    /// Turns one Smithy JSON AST into the neutral model.
    /// </summary>
    /// <remarks>
    /// The only code in this task that knows what Smithy is. The <c>specPath</c> is unused because
    /// an AST resolves every reference before it is written - there is nothing to load relative to
    /// the file, which is what <c>LoadExternalRefs</c> exists for on the OpenAPI side.
    /// </remarks>
    internal override ServiceSpecModel? Parse(
        string document, string fileName, string specPath, ICollection<string> diagnostics) =>
        SmithySpecParser.Parse(
            document,
            fileName,
            diagnostics,
            string.IsNullOrWhiteSpace(ServiceShapeId) ? null : ServiceShapeId.Trim());
}
