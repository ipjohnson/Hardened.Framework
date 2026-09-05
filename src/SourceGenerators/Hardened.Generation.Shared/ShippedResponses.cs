using System.Globalization;
using Hardened.Generation.Models;

namespace Hardened.Generation;

/// <summary>
/// The framework type a declared error response binds to, and the name it gets where none fits.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why binding rather than generating.</b> A declared error used to become one type per
/// operation and status - <c>GetPetNotFoundException</c> beside <c>GetPetLabelNotFoundException</c>,
/// the same class under two names. The thing that rule avoided is two responses in one set
/// resolving to one C# type, which is CS0457 at the point of use; but the per-status wrapper is
/// what solves that, not the operation prefix. <c>Hardened.Requests.Abstract.Responses</c> already
/// ships those wrappers, so a declared 404 with a <c>Problem</c> is
/// <c>NotFound&lt;Problem&gt;</c> - a type the code-first path already returns and a consumer
/// already knows.
/// </para>
/// <para>
/// <b>What still gets generated, and there are exactly three reasons.</b> The error carries a name
/// of its own - a Smithy error shape, or an OpenAPI <c>components/responses</c> key - and keeping
/// it is the whole point of <see cref="ErrorResponseModel.Name"/>. Or the response declares a
/// header, which a shipped wrapper has nowhere to put. Or the status has neither a shipped record
/// nor a marker in <c>Http</c>, which is only an unregistered code.
/// </para>
/// <para>
/// <b>Here rather than on an emitter</b> because four separate things have to reach the same
/// answer: the build task that writes the types, the Roslyn generator that writes the dispatch
/// switching over them, <see cref="ResponseSetPlan"/>, and the name allocator that arbitrates a
/// generated name against a schema's. The generator never sees the task's output, so a second
/// derivation of this is a switch arm naming a type nothing emitted.
/// </para>
/// </remarks>
internal static class ShippedResponses {

    /// <summary>Where every shipped response record and status marker lives.</summary>
    public const string Namespace = "Hardened.Requests.Abstract.Responses";

    /// <summary>The open generic that closes over a status marker for a code with no record.</summary>
    public const string StatusTypeName = "Status";

    /// <summary>The holder the status markers are nested in.</summary>
    public const string MarkerHolderName = "Http";

    /// <summary>
    /// One shipped response the build binds a declared error to.
    /// </summary>
    internal readonly struct Binding {

        public Binding(string typeName, string? marker, bool takesBody, bool hasBody, bool appliesHeaders) {
            TypeName = typeName;
            Marker = marker;
            TakesBody = takesBody;
            HasBody = hasBody;
            AppliesHeaders = appliesHeaders;
        }

        /// <summary>The record's simple name - <c>NotFound</c>, or <c>Status</c> for a marker.</summary>
        public string TypeName { get; }

        /// <summary>The marker nested in <c>Http</c>, where this is a <c>Status&lt;&gt;</c>.</summary>
        public string? Marker { get; }

        /// <summary>Whether the bound form carries the payload the description declared.</summary>
        /// <remarks>
        /// False for a bare form, whose body is the framework's own problem document rather than
        /// the declared schema - which is the same thing a declared response with no content got
        /// before, except that it was an <c>ErrorModel</c> naming a generated exception class.
        /// </remarks>
        public bool TakesBody { get; }

        /// <summary>Whether anything is serialized for it.</summary>
        public bool HasBody { get; }

        /// <summary>Whether it contributes headers through <c>IProvidesResponseHeaders</c>.</summary>
        public bool AppliesHeaders { get; }
    }

    /// <summary>
    /// The shipped response a declared error binds to, or null where a type has to be generated.
    /// </summary>
    /// <remarks>
    /// The one decision, asked by every caller rather than restated by any of them. A null answer
    /// is what <see cref="GeneratedName"/> then names.
    /// </remarks>
    public static Binding? For(ErrorResponseModel error) {
        // A name the description gave the error is the thing worth keeping, and no shipped record
        // can carry it. Smithy names every error; OpenAPI names one only through
        // components/responses.
        if (!string.IsNullOrEmpty(error.Name)) {
            return null;
        }

        // A declared header has nowhere to go on a shipped wrapper. NotFound<T> cannot carry a
        // Retry-After the way a generated case type can, and a header the document declares and
        // nothing sends is worse than an extra type.
        if (error.Headers.Count > 0) {
            return null;
        }

        return error.Ref == null
            ? Bodyless(error.StatusCode)
            : Carrying(error.StatusCode);
    }

    /// <summary>
    /// A declared error with a body, as the shipped generic form or a closed <c>Status&lt;&gt;</c>.
    /// </summary>
    private static Binding? Carrying(int statusCode) {
        var generic = GenericForm(statusCode);

        if (generic != null) {
            return new Binding(
                generic, marker: null, takesBody: true, hasBody: true,
                appliesHeaders: CarriesHeaders(statusCode));
        }

        var marker = Marker(statusCode);

        return marker == null
            ? null
            : new Binding(
                StatusTypeName, marker, takesBody: true, hasBody: true, appliesHeaders: false);
    }

    /// <summary>
    /// A declared error with no content, as the shipped bare form or a bodyless
    /// <c>Status&lt;&gt;</c>.
    /// </summary>
    private static Binding? Bodyless(int statusCode) {
        var bare = BareForm(statusCode);

        if (bare != null) {
            return new Binding(
                bare, marker: null, takesBody: false, hasBody: SendsABody(statusCode),
                appliesHeaders: CarriesHeaders(statusCode));
        }

        var marker = Marker(statusCode);

        return marker == null
            ? null
            : new Binding(
                StatusTypeName, marker, takesBody: false, hasBody: false, appliesHeaders: false);
    }

    /// <summary>
    /// The generic shipped record for a status - used as <c>NotFound&lt;Problem&gt;</c> - or null.
    /// </summary>
    /// <remarks>
    /// Every entry here is a <c>[HttpStatus]</c> record in <see cref="Namespace"/> implementing
    /// <c>ICarriesResponseBody</c>, so the declared payload goes on the wire unwrapped. A status
    /// absent from this and present in <see cref="BareForm"/> is one the framework refuses to put a
    /// body on: 304 and 406 both, and for stated reasons rather than by omission.
    /// </remarks>
    public static string? GenericForm(int statusCode) {
        switch (statusCode) {
            case 400: return "BadRequest";
            case 401: return "Unauthorized";
            case 402: return "PaymentRequired";
            case 403: return "Forbidden";
            case 404: return "NotFound";
            case 405: return "MethodNotAllowed";
            case 408: return "RequestTimeout";
            case 409: return "Conflict";
            case 410: return "Gone";
            case 412: return "PreconditionFailed";
            case 413: return "ContentTooLarge";
            case 415: return "UnsupportedMediaType";
            case 422: return "UnprocessableContent";
            case 428: return "PreconditionRequired";
            case 429: return "RateLimited";
            case 500: return "InternalServerError";
            case 501: return "NotImplemented";
            case 502: return "BadGateway";
            case 503: return "ServiceUnavailable";
            case 504: return "GatewayTimeout";
            default: return null;
        }
    }

    /// <summary>The bare shipped record for a status, or null.</summary>
    /// <summary>
    /// Whether the bare record for the status carries <c>Type</c>, <c>Title</c> and <c>Detail</c>,
    /// which every problem-kind record does and <c>MethodNotAllowed</c> and <c>NotAcceptable</c>
    /// do not.
    /// </summary>
    public static bool HasProblemMembers(int statusCode) =>
        GenericForm(statusCode) != null && statusCode != 405 && statusCode != 406;

    /// <summary>
    /// The <c>ProblemTypes</c> constant naming the status's problem kind, or null where the
    /// framework declares none. The constant is named after the record.
    /// </summary>
    public static string? ProblemType(int statusCode) =>
        HasProblemMembers(statusCode) ? GenericForm(statusCode) : null;

    /// <summary>
    /// Whether the bare record for the status has a <c>Default</c> instance - one built with a
    /// generic message and nothing a caller would have to supply. <c>RateLimited</c> needs a delay
    /// and <c>MethodNotAllowed</c> an <c>Allow</c>, so neither has one; <c>NotAcceptable</c> carries
    /// no message to be generic about.
    /// </summary>
    public static bool HasDefaultInstance(int statusCode) =>
        HasProblemMembers(statusCode) && statusCode != 429;

    /// <summary>
    /// The generic form's constructor arguments, from the body a bare record converts into and
    /// the record itself. Four records carry a header the generic form takes beside the body, in
    /// the order that form declares.
    /// </summary>
    public static string GenericArguments(int statusCode, string body, string record) {
        switch (statusCode) {
            case 401: return body + ", " + record + ".Challenge";
            case 405: return body + ", " + record + ".Allow";
            case 429: return record + ".RetryAfter, " + body;
            case 503: return body + ", " + record + ".After";
            default: return body;
        }
    }

    public static string? BareForm(int statusCode) =>
        statusCode == 304 ? "NotModified"
            : statusCode == 406 ? "NotAcceptable"
                : GenericForm(statusCode);

    /// <summary>Whether the shipped record for a status serializes anything.</summary>
    private static bool SendsABody(int statusCode) =>
        statusCode != 304 && statusCode != 405 && statusCode != 406;

    /// <summary>
    /// Whether the shipped record for a status implements <c>IProvidesResponseHeaders</c>.
    /// </summary>
    /// <remarks>
    /// Only the statuses whose response is not well formed without one: a 405's <c>Allow</c>, a
    /// 429's and a 503's <c>Retry-After</c>, a 304's <c>ETag</c>. Read here so the emitted dispatch
    /// calls <c>ApplyHeaders</c> for exactly those arms, which is the same thing
    /// <c>UnionResponseSelector</c> answers from the symbol when it can see one.
    /// </remarks>
    private static bool CarriesHeaders(int statusCode) =>
        statusCode == 304 || statusCode == 405 || statusCode == 429 || statusCode == 503;

    /// <summary>
    /// The <c>Http</c> marker for a registered status with no record of its own, or null.
    /// </summary>
    /// <remarks>
    /// The tail a table can never finish. These reach the wire as
    /// <c>Status&lt;Http.Locked, Problem&gt;</c>, which costs the framework a line per status
    /// instead of a record - and an application needing one that is not here declares its own
    /// marker beside them. Null is left for the codes nobody registered: 529 is the case, and it is
    /// why the escape hatch is a type argument rather than this list.
    /// </remarks>
    public static string? Marker(int statusCode) {
        switch (statusCode) {
            case 203: return "NonAuthoritativeInformation";
            case 205: return "ResetContent";
            case 206: return "PartialContent";
            case 207: return "MultiStatus";
            case 208: return "AlreadyReported";
            case 226: return "IMUsed";
            case 300: return "MultipleChoices";
            case 301: return "MovedPermanently";
            case 302: return "Found";
            case 303: return "SeeOther";
            case 305: return "UseProxy";
            case 307: return "TemporaryRedirect";
            case 308: return "PermanentRedirect";
            case 407: return "ProxyAuthenticationRequired";
            case 411: return "LengthRequired";
            case 414: return "UriTooLong";
            case 416: return "RangeNotSatisfiable";
            case 417: return "ExpectationFailed";
            case 418: return "ImATeapot";
            case 421: return "MisdirectedRequest";
            case 423: return "Locked";
            case 424: return "FailedDependency";
            case 425: return "TooEarly";
            case 426: return "UpgradeRequired";
            case 431: return "RequestHeaderFieldsTooLarge";
            case 451: return "UnavailableForLegalReasons";
            case 505: return "HttpVersionNotSupported";
            case 506: return "VariantAlsoNegotiates";
            case 507: return "InsufficientStorage";
            case 508: return "LoopDetected";
            case 510: return "NotExtended";
            case 511: return "NetworkAuthenticationRequired";
            default: return null;
        }
    }

    /// <summary>
    /// What a generated type for this error would like to be called, before uniqueness is applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The declared name where there is one, which is the whole of what keying by identity means: a
    /// Smithy <c>AccountNotFound</c> becomes <c>AccountNotFoundException</c>, once, shared by every
    /// operation that binds it - the type every other Smithy code generator emits from the same
    /// model.
    /// </para>
    /// <para>
    /// Otherwise the status and the payload schema, which is <c>DefaultErrorBody.FieldName</c>'s
    /// existing scheme - <c>NotFoundProblem</c>. Not the operation: two operations declaring one
    /// 404 over one schema want one type, and the operation prefix is exactly the information that
    /// made them two.
    /// </para>
    /// </remarks>
    public static string GeneratedName(ErrorResponseModel error) {
        if (!string.IsNullOrEmpty(error.Name)) {
            return NamingHelper.ToPascalCase(error.Name!);
        }

        var status = StatusName(error.StatusCode);

        return error.Ref == null
            ? status
            : status + NamingHelper.ToPascalCase(TypeMapper.GetRefName(error.Ref!));
    }

    /// <summary>
    /// The key two declared errors are the same generated type by.
    /// </summary>
    /// <remarks>
    /// Not the desired name: two errors wanting one name and carrying different payloads are two
    /// types, and collapsing them by name would emit one record and reference it for both. The
    /// declared headers are in the key for the same reason - a 429 with a <c>Retry-After</c> and one
    /// without are different constructors.
    /// </remarks>
    public static string GeneratedKey(ErrorResponseModel error) {
        var key = GeneratedName(error) + "|" +
                  error.StatusCode.ToString(CultureInfo.InvariantCulture) + "|" +
                  (error.Ref ?? "");

        foreach (var header in error.Headers) {
            key += "|" + header.Name;
        }

        return key;
    }

    /// <summary>
    /// The status as a name, for every generated type that is named after one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One table, where there were two.</b> The throws-mode emitter and the response-set plan
    /// each kept their own, with a doc comment on each explaining that the two must be free to
    /// diverge - and they had, so the same status generated two different names depending on which
    /// response model the operation used: <c>UnprocessableEntity</c> against
    /// <c>UnprocessableContent</c> for 422, <c>PayloadTooLarge</c> against nothing at all for 413,
    /// and 428 present in one and absent from the other. A generated name is API; having two of
    /// them for one status is not a freedom worth keeping.
    /// </para>
    /// <para>
    /// The names are RFC 9110's current ones, and for a status with a shipped record they are that
    /// record's name - so a generated residual type and the framework type beside it read the same.
    /// That is what makes 429 <c>RateLimited</c> here rather than <c>TooManyRequests</c>.
    /// </para>
    /// <para>
    /// A status with no registered name keeps its number. It reads badly and it is unambiguous, and
    /// a description using 529 deserves a type as much as one using 404.
    /// </para>
    /// </remarks>
    public static string StatusName(int statusCode) {
        var shipped = BareForm(statusCode);

        if (shipped != null) {
            return shipped;
        }

        var marker = Marker(statusCode);

        if (marker != null) {
            return marker;
        }

        switch (statusCode) {
            // The 2xx names a success case type takes. Not in the marker table, because these are
            // the statuses the framework does ship a record for.
            case 200: return "Ok";
            case 201: return "Created";
            case 202: return "Accepted";
            case 204: return "NoContent";
            default: return "Status" + statusCode.ToString(CultureInfo.InvariantCulture);
        }
    }
}
