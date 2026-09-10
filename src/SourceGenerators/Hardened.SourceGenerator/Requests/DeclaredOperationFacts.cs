using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// What the declarations covering one handler say about its document, before it is known which of
/// them reach the operation.
/// </summary>
/// <remarks>
/// <para>
/// Two front ends read this and only one of them knows the operation's verb when it reads. An
/// attribute-routed handler carries its route on the same method the declarations are on; a
/// described one gets its verb from the contract, long after <c>HandlerSelector</c> has walked the
/// implementation's syntax. So the reading is separated from the narrowing: this holds everything
/// found, and <see cref="For"/> answers for one operation.
/// </para>
/// <para>
/// Which also keeps this comparable. It reaches a Roslyn incremental cache key through
/// <c>HandlerInfo</c>, and a value that narrowed at read time would depend on facts that pass did
/// not have.
/// </para>
/// </remarks>
internal sealed class DeclaredOperationFacts : IEquatable<DeclaredOperationFacts>, IEntryPointFilterFacts {

    public static readonly DeclaredOperationFacts Empty = new(
        Array.Empty<ScopedRefusal>(),
        Array.Empty<ScopedResponseHeader>(),
        Array.Empty<ScopedRequestHeader>());

    public DeclaredOperationFacts(
        IReadOnlyList<ScopedRefusal> refusals,
        IReadOnlyList<ScopedResponseHeader> responseHeaders,
        IReadOnlyList<ScopedRequestHeader> requestHeaders) {
        Refusals = refusals;
        ResponseHeaders = responseHeaders;
        RequestHeaders = requestHeaders;
    }

    public IReadOnlyList<ScopedRefusal> Refusals { get; }

    public IReadOnlyList<ScopedResponseHeader> ResponseHeaders { get; }

    public IReadOnlyList<ScopedRequestHeader> RequestHeaders { get; }

    public bool IsEmpty =>
        Refusals.Count == 0 && ResponseHeaders.Count == 0 && RequestHeaders.Count == 0;

    /// <summary>The subset reaching an operation with this verb and this response shape.</summary>
    public OperationDeclarations For(string? httpMethod, bool streams) =>
        new(Refusals.Where(entry => entry.Scope.Reaches(httpMethod, streams))
                .Select(entry => entry.Response).ToList(),
            ResponseHeaders.Where(entry => entry.Scope.Reaches(httpMethod, streams)).ToList(),
            RequestHeaders.Where(entry => entry.Scope.Reaches(httpMethod, streams)).ToList());

    public bool Equals(DeclaredOperationFacts? other) =>
        other is not null &&
        Refusals.SequenceEqual(other.Refusals) &&
        ResponseHeaders.SequenceEqual(other.ResponseHeaders) &&
        RequestHeaders.SequenceEqual(other.RequestHeaders);

    public override bool Equals(object? obj) => Equals(obj as DeclaredOperationFacts);

    public override int GetHashCode() {
        unchecked {
            var hash = Refusals.Count;

            hash = (hash * 397) ^ ResponseHeaders.Count;
            hash = (hash * 397) ^ RequestHeaders.Count;

            foreach (var refusal in Refusals) {
                hash = (hash * 397) ^ refusal.GetHashCode();
            }

            foreach (var header in ResponseHeaders) {
                hash = (hash * 397) ^ header.GetHashCode();
            }

            foreach (var header in RequestHeaders) {
                hash = (hash * 397) ^ header.GetHashCode();
            }

            return hash;
        }
    }
}

/// <summary>
/// The reach a declaration stated for itself, as its <c>Methods</c> and <c>NotWhenStreaming</c>
/// were written.
/// </summary>
/// <remarks>
/// A declaration written on one method describes that method. Written on a class or an assembly it
/// describes every operation under it - and a filter that stands down on some of them then has the
/// document claiming a status or a header those operations cannot produce. <c>[ConditionalGet]</c>
/// is the case: on a controller it installs on the reads and on nothing else, so a 304 published on
/// the writes beside them is a lie a generated client would write a branch for. So a declaration
/// states its own reach and this applies it - which keeps the rule that nothing here knows what a
/// filter does, because the filter says where it applies in the same place it says what it answers.
/// </remarks>
internal readonly struct DeclaredScope : IEquatable<DeclaredScope> {

    public DeclaredScope(string? methods, bool notWhenStreaming) {
        Methods = methods;
        NotWhenStreaming = notWhenStreaming;
    }

    public string? Methods { get; }

    public bool NotWhenStreaming { get; }

    /// <summary>
    /// Whether this reaches an operation with that verb and that response shape.
    /// </summary>
    /// <remarks>
    /// An unwritten restriction reaches everything, which is the ordinary case and the one every
    /// declaration had before this existed. Matching tolerates spaces, because <c>"GET, HEAD"</c>
    /// is how somebody writes it.
    /// </remarks>
    public bool Reaches(string? httpMethod, bool streams) {
        if (NotWhenStreaming && streams) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Methods) || string.IsNullOrEmpty(httpMethod)) {
            return true;
        }

        foreach (var candidate in Methods!.Split(',')) {
            if (string.Equals(candidate.Trim(), httpMethod, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    public bool Equals(DeclaredScope other) =>
        Methods == other.Methods && NotWhenStreaming == other.NotWhenStreaming;

    public override bool Equals(object? obj) => obj is DeclaredScope other && Equals(other);

    public override int GetHashCode() =>
        unchecked(((Methods?.GetHashCode() ?? 0) * 397) ^ NotWhenStreaming.GetHashCode());
}

/// <summary>A status a declaration can answer with, and the operations it reaches.</summary>
internal sealed class ScopedRefusal : IEquatable<ScopedRefusal> {

    public ScopedRefusal(ResponseSchemaModel response, DeclaredScope scope) {
        Response = response;
        Scope = scope;
    }

    public ResponseSchemaModel Response { get; }

    public DeclaredScope Scope { get; }

    public bool Equals(ScopedRefusal? other) =>
        other is not null && Response.Equals(other.Response) && Scope.Equals(other.Scope);

    public override bool Equals(object? obj) => Equals(obj as ScopedRefusal);

    public override int GetHashCode() =>
        unchecked((Response.GetHashCode() * 397) ^ Scope.GetHashCode());
}

/// <summary>A header a declaration says a response carries, and the operations it reaches.</summary>
internal sealed class ScopedResponseHeader : IEquatable<ScopedResponseHeader> {

    public ScopedResponseHeader(int status, string name, string? description, DeclaredScope scope) {
        Status = status;
        Name = name;
        Description = description;
        Scope = scope;
    }

    public int Status { get; }

    public string Name { get; }

    public string? Description { get; }

    public DeclaredScope Scope { get; }

    public bool Equals(ScopedResponseHeader? other) =>
        other is not null &&
        Status == other.Status && Name == other.Name && Description == other.Description &&
        Scope.Equals(other.Scope);

    public override bool Equals(object? obj) => Equals(obj as ScopedResponseHeader);

    public override int GetHashCode() {
        unchecked {
            var hash = Status;

            hash = (hash * 397) ^ Name.GetHashCode();
            hash = (hash * 397) ^ (Description?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ Scope.GetHashCode();

            return hash;
        }
    }
}

/// <summary>A header a declaration says it reads, and the operations it reaches.</summary>
internal sealed class ScopedRequestHeader : IEquatable<ScopedRequestHeader> {

    public ScopedRequestHeader(string name, string? description, DeclaredScope scope) {
        Name = name;
        Description = description;
        Scope = scope;
    }

    public string Name { get; }

    public string? Description { get; }

    public DeclaredScope Scope { get; }

    public bool Equals(ScopedRequestHeader? other) =>
        other is not null &&
        Name == other.Name && Description == other.Description && Scope.Equals(other.Scope);

    public override bool Equals(object? obj) => Equals(obj as ScopedRequestHeader);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();

            hash = (hash * 397) ^ (Description?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ Scope.GetHashCode();

            return hash;
        }
    }
}

/// <summary>What the declarations covering one operation say about that operation's document.</summary>
internal sealed class OperationDeclarations {

    public OperationDeclarations(
        IReadOnlyList<ResponseSchemaModel> refusals,
        IReadOnlyList<ScopedResponseHeader> responseHeaders,
        IReadOnlyList<ScopedRequestHeader> requestHeaders) {
        Refusals = refusals;
        ResponseHeaders = responseHeaders;
        RequestHeaders = requestHeaders;
    }

    public IReadOnlyList<ResponseSchemaModel> Refusals { get; }

    public IReadOnlyList<ScopedResponseHeader> ResponseHeaders { get; }

    public IReadOnlyList<ScopedRequestHeader> RequestHeaders { get; }

    /// <summary>The header parameters, as the document model carries them.</summary>
    public IReadOnlyList<DeclaredHeaderParameterModel> HeaderParameters() {
        if (RequestHeaders.Count == 0) {
            return Array.Empty<DeclaredHeaderParameterModel>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DeclaredHeaderParameterModel>();

        foreach (var header in RequestHeaders) {
            if (seen.Add(header.Name)) {
                result.Add(new DeclaredHeaderParameterModel(header.Name, header.Description));
            }
        }

        return result;
    }

    /// <summary>
    /// <paramref name="responses"/> with each declared header attached to the status it names.
    /// </summary>
    /// <remarks>
    /// A header on a status nothing declares is dropped. A header is a fact about a response, so
    /// one naming a status the operation never answers describes nothing - and synthesizing the
    /// response to hang it on would publish a status the handler cannot produce.
    /// </remarks>
    public IReadOnlyList<ResponseSchemaModel> WithHeaders(
        IReadOnlyList<ResponseSchemaModel> responses) {
        if (ResponseHeaders.Count == 0 || responses.Count == 0) {
            return responses;
        }

        var result = new List<ResponseSchemaModel>(responses.Count);

        foreach (var response in responses) {
            var added = Headers(response.Status, response.Headers);

            result.Add(added == null
                ? response
                : new ResponseSchemaModel(response.Status, response.Description, response.Schema) {
                    Headers = added
                });
        }

        return result;
    }

    /// <summary>
    /// The status's declared headers plus the ones already on it, or null where nothing was added.
    /// </summary>
    /// <remarks>
    /// The response's own win on a name: a contract that declared the header said more about it
    /// than a filter's blanket statement can.
    /// </remarks>
    public IReadOnlyList<ResponseHeaderModel>? Headers(
        int status, IReadOnlyList<ResponseHeaderModel> existing) {
        List<ResponseHeaderModel>? merged = null;

        foreach (var declared in ResponseHeaders) {
            if (declared.Status != status || Names(existing, declared.Name) ||
                Names(merged, declared.Name)) {
                continue;
            }

            (merged ??= new List<ResponseHeaderModel>(existing)).Add(new ResponseHeaderModel {
                Name = declared.Name,
                ParameterName = Generation.NamingHelper.ToPascalCase(declared.Name.Replace("-", "")),
                Description = declared.Description
            });
        }

        return merged;
    }

    private static bool Names(IReadOnlyList<ResponseHeaderModel>? headers, string name) {
        if (headers == null) {
            return false;
        }

        foreach (var header in headers) {
            if (string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
