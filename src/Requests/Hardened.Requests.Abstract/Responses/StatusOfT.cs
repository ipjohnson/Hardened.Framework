namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A response at a status the framework ships no record for, carrying a body.
/// </summary>
/// <remarks>
/// <para>
/// The escape hatch behind the shipped records. <c>NotFound&lt;Problem&gt;</c> is the answer where
/// one exists; this is the answer where none does, and it costs the application one line - a
/// marker struct - however many operations declare the status.
/// </para>
/// <para>
/// <b>Closed per status, which is the property that matters in a response set.</b>
/// <c>Status&lt;Http.ImATeapot, Problem&gt;</c> and <c>Status&lt;Http.Locked, Problem&gt;</c> are
/// two distinct types over one payload schema, so a set declaring both compiles where
/// <c>Response&lt;Pet, Problem, Problem&gt;</c> is CS0457. That is the same thing the per-status
/// wrappers buy, reached without a record per status.
/// </para>
/// <para>
/// <b>The body is <see cref="Body"/>, not this record</b>, through
/// <see cref="ICarriesResponseBody"/> - the same treatment the generic shipped records get, so a
/// declared payload goes on the wire unwrapped rather than nested under a member.
/// </para>
/// <para>
/// <b>The status is an explicit interface implementation, and it has to be.</b> A member called
/// <c>Status</c> on a type called <c>Status</c> is CS0542 - the same rule <c>SpecDiagnostics</c>
/// reports as <c>020</c> for a schema whose property matches its own name. The pipeline reads the
/// status through <see cref="IHttpStatusResponse"/> anyway; code holding the concrete type reads
/// the number off the marker, as <c>TCode.Status</c>.
/// </para>
/// </remarks>
/// <typeparam name="TCode">The status, as a marker type. See <see cref="Http"/>.</typeparam>
/// <typeparam name="TBody">What the response carries.</typeparam>
public sealed record Status<TCode, TBody>(TBody Body)
    : IHttpStatusResponse, ICarriesResponseBody, IResponseExpectation<Status<TCode, TBody>>
    where TCode : IStatusCode {

    public static int StatusCode => TCode.Status;

    int IHttpStatusResponse.Status => StatusCode;

    object? ICarriesResponseBody.Body => Body;

    public static Status<TCode, TBody> FromResponse(
        object? body, IReadOnlyDictionary<string, string> headers) =>
        new(ResponseExpectation.Body<TBody>(body));
}

/// <summary>
/// A response at a status the framework ships no record for, carrying nothing.
/// </summary>
/// <remarks>
/// The bodyless half of <see cref="Status{TCode, TBody}"/>, for a declared status whose whole
/// answer is the code. A parameterless record rather than
/// <c>Status&lt;TCode, object?&gt;</c> holding null: the second serializes <c>null</c> as the body,
/// which is a response some clients reject and none expected.
/// <para>
/// <see cref="IHttpStatusResponse.Status"/> is implemented explicitly for the reason
/// <see cref="Status{TCode, TBody}"/> gives: CS0542 forbids a member matching its enclosing type.
/// </para>
/// </remarks>
/// <typeparam name="TCode">The status, as a marker type. See <see cref="Http"/>.</typeparam>
public sealed record Status<TCode> : IHttpStatusResponse, IResponseExpectation<Status<TCode>>
    where TCode : IStatusCode {

    public static int StatusCode => TCode.Status;

    int IHttpStatusResponse.Status => StatusCode;

    public bool HasBody => false;

    public static Status<TCode> FromResponse(
        object? body, IReadOnlyDictionary<string, string> headers) => new();
}
