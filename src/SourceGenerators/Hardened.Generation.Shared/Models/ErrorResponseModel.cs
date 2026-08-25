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
               Headers.SequenceEqual(other.Headers);
    }

    public override bool Equals(object? obj) => Equals(obj as ErrorResponseModel);

    public override int GetHashCode() {
        unchecked {
            return (StatusCode * 397) ^ (Ref?.GetHashCode() ?? 0);
        }
    }
}
