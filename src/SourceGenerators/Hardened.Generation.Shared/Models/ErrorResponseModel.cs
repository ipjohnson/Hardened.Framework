using System.Collections.Generic;
using System.Linq;

namespace Hardened.Generation.Models;

/// <summary>
/// A non-2xx response the specification declares.
/// </summary>
/// <remarks>
/// Every one of these used to be discarded: the parser took the lowest 2xx and stopped, so a
/// document could describe a 404 and its payload in detail and the generated code would contain no
/// trace of either.
/// </remarks>
internal class ErrorResponseModel : IEquatable<ErrorResponseModel> {
    public int StatusCode { get; set; }

    /// <summary>The declared body's schema, or null where the response has no content.</summary>
    public string? Ref { get; set; }

    public string? Description { get; set; }

    /// <summary>The name the description gave this error, or null where it gave none.</summary>
    /// <remarks>
    /// <para>
    /// <b>The field the front ends differ on, and the reason one naming rule cannot serve both.</b>
    /// In Smithy an error <em>is</em> a named shape: <c>bank.smithy</c> declares
    /// <c>AccountNotFound</c> once and binds it to two operations, so this is always set and the
    /// generated type is named after it - which is what every other Smithy code generator emits
    /// from the same model, and what stops a Hardened server and an AWS-generated client disagreeing
    /// about what the error is called.
    /// </para>
    /// <para>
    /// In OpenAPI a response is usually anonymous - a status, a description and a schema - and this
    /// stays null, so the error binds to a shipped wrapper instead. It is set only where
    /// <c>components/responses</c> gave the response a key, which is the author naming it.
    /// </para>
    /// <para>
    /// Not the schema's name. Two error shapes can carry one schema and one shape can be reused
    /// across statuses; the schema names the payload and this names the error.
    /// </para>
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// The case type generated for this error, or null where it binds to a shipped response.
    /// </summary>
    /// <remarks>
    /// Allocated by <c>NameAllocator</c> against the same scope the schemas take their names from,
    /// and carried here rather than re-derived: a Smithy error shape wants the name its payload
    /// record already has, so the arbitration has an answer only one pass can make. Read by the
    /// emitter that writes the type and by the generator that writes the switch over it, which run
    /// in different processes and meet only in the generated code.
    /// </remarks>
    public string? TypeName { get; set; }

    /// <summary>
    /// The exception type generated for this error in throws mode, or null where it binds.
    /// </summary>
    /// <remarks>
    /// Allocated separately from <see cref="TypeName"/> rather than suffixed onto it, because the
    /// collision the case type loses - a Smithy shape name the payload record already holds - is
    /// one <c>AccountNotFoundException</c> never has. Suffixing the arbitrated answer would give
    /// throws mode a name it had no reason to accept.
    /// </remarks>
    public string? ExceptionTypeName { get; set; }

    /// <summary>The headers this response declares it carries.</summary>
    /// <remarks>
    /// A declared error carries headers as readily as a success does - <c>Retry-After</c> on a 429
    /// and a 503 is the case RFC 9110 names, and a <c>WWW-Authenticate</c> on a 401 is the one the
    /// framework already writes by hand. An error case is always a wrapper, so unlike a success
    /// there is nothing to decide here: the type to hang the interface on already exists.
    /// </remarks>
    public List<ResponseHeaderModel> Headers { get; } = new();

    public bool Equals(ErrorResponseModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return StatusCode == other.StatusCode && Ref == other.Ref && Description == other.Description &&
               Name == other.Name && TypeName == other.TypeName &&
               ExceptionTypeName == other.ExceptionTypeName &&
               Headers.SequenceEqual(other.Headers);
    }

    public override bool Equals(object? obj) => Equals(obj as ErrorResponseModel);

    public override int GetHashCode() {
        unchecked {
            return (StatusCode * 397) ^ (Ref?.GetHashCode() ?? 0);
        }
    }
}
