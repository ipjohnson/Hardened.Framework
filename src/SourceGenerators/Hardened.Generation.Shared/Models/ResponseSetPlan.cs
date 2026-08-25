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
    /// except its return type: Standard mode reaches its other statuses by throwing, and a throw
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

        return (responseModel != SpecResponseModel.Standard || declaresMultipleSuccesses) &&
               !operation.RawBytesResponse &&
               operation.ItemSchemaRef == null &&
               (operation.ErrorResponses.Count > 0 || declaresMultipleSuccesses);
    }

    /// <summary>The name the service interface returns for this operation.</summary>
    /// <remarks>
    /// Public because <c>ServiceInterfaceEmitter</c> names it in a signature and this is the only
    /// definition of the scheme. Deriving it a second time there is how a generated type and the
    /// signature that returns it come to disagree.
    /// </remarks>
    public static string ContainerName(OperationModel operation) =>
        operation.MethodName + "Response";

    /// <summary>The case type for one declared status.</summary>
    public static string CaseName(OperationModel operation, int statusCode) =>
        operation.MethodName + StatusName(statusCode);

    /// <summary>
    /// The status's name, on the same scheme <c>ErrorResponseEmitter</c> uses.
    /// </summary>
    /// <remarks>
    /// Duplicated from that emitter rather than shared, because the two schemes must be free to
    /// diverge without one silently renaming the other's types - and a generated type name is API.
    /// </remarks>
    private static string StatusName(int statusCode) {
        switch (statusCode) {
            // The 2xx names match the built-in response types a code-first handler returns -
            // Created, Accepted, NoContent - so the same status reads the same in both directions.
            // They were absent because only errors were ever wrapped; a success was named by its
            // schema or not carried at all.
            case 200: return "Ok";
            case 201: return "Created";
            case 202: return "Accepted";
            case 203: return "NonAuthoritative";
            case 204: return "NoContent";
            case 205: return "ResetContent";
            case 206: return "PartialContent";
            case 400: return "BadRequest";
            case 401: return "Unauthorized";
            case 403: return "Forbidden";
            case 404: return "NotFound";
            case 405: return "MethodNotAllowed";
            case 406: return "NotAcceptable";
            case 409: return "Conflict";
            case 410: return "Gone";
            case 412: return "PreconditionFailed";
            case 415: return "UnsupportedMediaType";
            case 422: return "UnprocessableContent";
            case 429: return "TooManyRequests";
            case 500: return "InternalServerError";
            case 503: return "ServiceUnavailable";
            default: return "Status" + statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }


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
        !HasNamedSuccessPayload(operation);
}
