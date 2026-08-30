using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;

namespace Hardened.SourceGenerator.Models.Request;

/// <summary>
/// One response a handler declares: the status it answers with, and the body it sends.
/// </summary>
/// <remarks>
/// <para>
/// The code-first analogue of an <c>OperationModel</c>'s <c>SuccessStatusCode</c> and
/// <c>ErrorResponses</c> together, and deliberately one list rather than a flat success plus a list
/// of the rest. The specification-first model splits them because every consumer of its success
/// response reads the flat fields individually; nothing here does, and a document writer that has
/// to merge two shapes back into one <c>responses</c> object is a place for them to disagree.
/// </para>
/// <para>
/// <see cref="Schema"/> is null for a status that sends nothing. That is not the same as a status
/// with an unknown body: a 204 declares no content, and a document saying so is what stops a
/// generated client from waiting for one.
/// </para>
/// </remarks>
public sealed class ResponseSchemaModel : System.IEquatable<ResponseSchemaModel> {

    public ResponseSchemaModel(int status, string description, HandlerSchema? schema) {
        Status = status;
        Description = description;
        Schema = schema;
    }

    /// <summary>The status this response is keyed under in the document.</summary>
    public int Status { get; }

    /// <summary>
    /// The response's <c>description</c>, which OpenAPI requires of every response object.
    /// </summary>
    public string Description { get; }

    /// <summary>The body's schema, or null where the status carries none.</summary>
    public HandlerSchema? Schema { get; }

    /// <summary>
    /// The headers the contract declares this response carries, for the published document.
    /// </summary>
    /// <remarks>
    /// The runtime already applies these - the generated case type takes each as a constructor
    /// parameter - so a document that omitted them described a response as bare that always
    /// carries its Location. Empty for a response declaring none.
    /// </remarks>
    internal IReadOnlyList<ResponseHeaderModel> Headers { get; set; } =
        System.Array.Empty<ResponseHeaderModel>();

    /// <summary>
    /// By value, because this reaches <c>RequestHandlerModel</c>'s equality and that is a Roslyn
    /// incremental cache key. A reference comparison here would report two identical response sets
    /// as different on every edit, and - worse - is one refactor away from reporting two different
    /// ones as the same.
    /// </summary>
    public bool Equals(ResponseSchemaModel? other) =>
        other is not null &&
        Status == other.Status &&
        Description == other.Description &&
        Equals(Schema, other.Schema) &&
        Headers.SequenceEqual(other.Headers);

    public override bool Equals(object? obj) => Equals(obj as ResponseSchemaModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Status;

            hash = (hash * 397) ^ Description.GetHashCode();
            hash = (hash * 397) ^ (Schema?.GetHashCode() ?? 0);

            return hash;
        }
    }
}
