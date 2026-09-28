namespace Hardened.Generation.Models;

/// <summary>
/// A member a paging trait names that the operation cannot carry the protocol in.
/// </summary>
/// <remarks>
/// <para>
/// Recorded by the Smithy parser for <c>@paginated</c>, and reported by <c>SpecDiagnostics</c> for
/// the reason <see cref="DanglingReferenceModel"/> is: the members are generated as ordinary
/// members, so nothing in the model says afterwards which of them the trait named.
/// </para>
/// <para>
/// Not serialized into the model file, and absent from the model's equality. The build stops on
/// one of these, so nothing downstream reads a model that carries any.
/// </para>
/// </remarks>
internal sealed class PaginationMismatchModel
{
    public PaginationMismatchModel(string operation, string detail)
    {
        Operation = operation;
        Detail = detail;
    }

    /// <summary>The operation carrying the trait, as the model names it.</summary>
    public string Operation { get; }

    /// <summary>What is wrong, as a clause that follows the operation's name.</summary>
    public string Detail { get; }
}
