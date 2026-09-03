using Hardened.Generation;

namespace Hardened.Generation.Models;

/// <summary>
/// Which operations answer with a declared response set, and what the generated types are called.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than on <c>UnionResponseEmitter</c> because two halves of the build need the same
/// answers and only one of them compiles the emitters. The build task writes the container and its
/// case types; the generator writes the dispatch that switches over them. When only the emitter
/// could name them, the generator emitted a plain assignment for every operation - so a handler
/// whose signature returned a response set put the wrapper on the wire, nested under its own Value,
/// at whatever status the operation would have answered anyway.
/// </para>
/// <para>
/// One definition of each, for the reason the emitter's own remarks already gave about
/// <c>ContainerName</c>: deriving a generated type's name a second time is how the type and the
/// code that names it come to disagree.
/// </para>
/// </remarks>
internal static class ResponseSetPlan {

    /// <summary>
    /// Whether this operation answers with a declared response set rather than one type and throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per operation rather than per service, because the second clause is not a preference the
    /// module expressed. An operation declaring more than one success has nowhere to put the second
    /// except its return type: Throws mode reaches its other statuses by throwing, and a throw
    /// carries a failure - there is no way to throw a 202. Left alone it would emit a document
    /// describing a status the handler cannot produce.
    /// </para>
    /// <para>
    /// Public and used by <c>ServiceInterfaceEmitter</c> and <c>SpecFileEmitter</c> both, because
    /// the signature and the type it names have to be decided by one answer. The signature is
    /// emitted in one place and the type in another, and the failure when they disagree is a
    /// generated method returning a type nothing emitted.
    /// </para>
    /// <para>
    /// Raw bytes and a streamed body are excluded here rather than at the call sites: the first is
    /// a payload the application already holds encoded and the second is many bodies, so neither is
    /// one of several responses.
    /// </para>
    /// </remarks>
    public static bool RequiresResponseSet(OperationModel operation, SpecResponseModel responseModel) {
        var declaresMultipleSuccesses = operation.SuccessResponses.Count > 1;

        // A declared header forces a set the same way a second success does, and for the same
        // reason: the bare payload type cannot express it. Throws mode is already dragged into a
        // set by a second success - "there is no way to throw a 202" - and there is no way to put a
        // Location on a returned Pet either. Without this the fix reaches two response models of
        // three and throws-mode documents go on declaring headers nothing sends.
        var declaresResponseHeaders = DeclaresResponseHeaders(operation);

        return (responseModel != SpecResponseModel.Throws ||
                declaresMultipleSuccesses ||
                declaresResponseHeaders) &&
               !operation.RawBytesResponse &&
               operation.ItemSchemaRef == null &&
               (operation.ErrorResponses.Count > 0 ||
                declaresMultipleSuccesses ||
                declaresResponseHeaders);
    }

    /// <summary>
    /// Whether the primary success's payload type carries headers of its own.
    /// </summary>
    public static bool PrimarySuccessCarriesHeaders(OperationModel operation) {
        foreach (var response in operation.SuccessResponses) {
            if (response.StatusCode == operation.SuccessStatusCode) {
                return response.HeadersOnPayload && response.Headers.Count > 0;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether any response this operation declares needs a case type built to carry a header.
    /// </summary>
    /// <remarks>
    /// A payload that carries its own headers needs nothing: the type the handler already returns
    /// implements the interface, so the response set it would otherwise be forced into buys nothing
    /// and costs the signature.
    /// </remarks>
    public static bool DeclaresResponseHeaders(OperationModel operation) {
        foreach (var response in operation.SuccessResponses) {
            if (response.Headers.Count > 0 && !response.HeadersOnPayload) {
                return true;
            }
        }

        foreach (var response in operation.ErrorResponses) {
            if (response.Headers.Count > 0) {
                return true;
            }
        }

        return false;
    }

    /// <summary>The name the service interface returns for this operation.</summary>
    /// <remarks>
    /// Public because <c>ServiceInterfaceEmitter</c> names it in a signature and this is the only
    /// definition of the scheme. Deriving it a second time there is how a generated type and the
    /// signature that returns it come to disagree.
    /// </remarks>
    public static string ContainerName(OperationModel operation) =>
        operation.MethodName + "Response";

    /// <summary>The case type for one declared success status.</summary>
    /// <remarks>
    /// <para>
    /// Still named for the operation, unlike the error cases beside it. A success case carries the
    /// operation's own payload shape - <c>GetLabelOk(string Body)</c> - so two operations declaring
    /// a 200 have nothing to share, where two operations declaring one 404 over one schema want one
    /// type.
    /// </para>
    /// <para>
    /// A declared error takes its name from <see cref="ErrorCaseName"/> instead, or binds to a
    /// shipped response and gets no generated type at all.
    /// </para>
    /// </remarks>
    public static string CaseName(OperationModel operation, int statusCode) =>
        operation.MethodName + ShippedResponses.StatusName(statusCode);

    /// <summary>
    /// The case type for one declared error, or null where it binds to a shipped response.
    /// </summary>
    /// <remarks>
    /// Read off the model rather than composed here. <c>NameAllocator</c> decided it, because
    /// deciding it needs the whole document - a Smithy error shape wants the name its own payload
    /// record already holds, and only a pass that sees both can arbitrate that.
    /// </remarks>
    public static string? ErrorCaseName(ErrorResponseModel error) => error.TypeName;

    /// <summary>
    /// Whether the operation's primary success has a payload the union can name directly.
    /// </summary>
    /// <remarks>
    /// The one definition of it. <c>SuccessBranchType</c> asks so it knows whether to add a branch;
    /// <see cref="NeedsSuccessCaseType"/> asks so it knows whether to wrap one instead. Two copies
    /// of this condition is an operation that gets a branch and a wrapper, or neither.
    /// </remarks>
    public static bool HasNamedSuccessPayload(OperationModel operation) =>
        operation.ResponseRef != null ||
        (operation.ResponseIsArray && operation.ResponseArrayItemsRef != null);

    /// <summary>
    /// Whether a declared success needs a case type of its own rather than being named by its schema.
    /// </summary>
    /// <remarks>
    /// The primary success is the operation's own payload type, so it needs no wrapper - unless it
    /// has no payload to name, which is 204 and every other bodyless success. Every other success is
    /// wrapped whatever its body, because the wrapper is what carries the status: two successes
    /// sharing one schema would otherwise put the same type in the union twice, and two identical
    /// conversions are ambiguous at the use site.
    /// </remarks>
    public static bool NeedsSuccessCaseType(OperationModel operation, SuccessResponseModel response) =>
        response.StatusCode != operation.SuccessStatusCode ||
        !HasNamedSuccessPayload(operation) ||
        (response.Headers.Count > 0 && !response.HeadersOnPayload);

    /// <summary>
    /// Whether the primary success reaches the union as the payload type itself.
    /// </summary>
    /// <remarks>
    /// It does, unless it declares headers. The payload type cannot carry them: <c>Part</c> is the
    /// same type a 200 with no <c>Location</c> answers with, so implementing
    /// <c>IProvidesResponseHeaders</c> on it would put the 201's header on every response that ever
    /// sends a Part. A declared header is therefore what turns the primary success into a wrapper -
    /// and only a declared header does, so an operation that declares none keeps the bare payload
    /// and the signature it already had.
    /// </remarks>
    public static bool PrimarySuccessIsBarePayload(OperationModel operation) {
        if (!HasNamedSuccessPayload(operation)) {
            return false;
        }

        foreach (var response in operation.SuccessResponses) {
            if (response.StatusCode == operation.SuccessStatusCode) {
                return response.Headers.Count == 0 || response.HeadersOnPayload;
            }
        }

        return true;
    }
}
