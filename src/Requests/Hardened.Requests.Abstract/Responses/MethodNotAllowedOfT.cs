using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A 405 carrying a body the caller supplies, and the methods the path does answer.
/// </summary>
/// <remarks>
/// <para>
/// The generic form of <see cref="MethodNotAllowed"/>, for a caller who already has a payload type
/// and does not want a bodyless refusal. <c>IMethodNotAllowedHandler</c>'s own documentation names
/// wanting a body on a 405 as a reason an application replaces the router's handler; this is the
/// same want, expressed in a return type.
/// </para>
/// <para>
/// <b>The body is <see cref="Body"/>, not this record.</b> It implements
/// <see cref="ICarriesResponseBody"/>, so the generated dispatch sends the payload rather than a
/// wrapper with the payload nested inside it.
/// </para>
/// <para>
/// <see cref="Allow"/> stays required and stays after the body, so adding a payload to a 405 does
/// not make the header the caller can drop. A 405 without it is correct and useless.
/// </para>
/// </remarks>
[HttpStatus(405)]
public sealed record MethodNotAllowed<T>(T Body, string Allow)
    : IHttpStatusResponse, ICarriesResponseBody, IProvidesResponseHeaders,
        IResponseExpectation<MethodNotAllowed<T>> {

    public static int StatusCode => 405;

    public int Status => StatusCode;

    object? ICarriesResponseBody.Body => Body;

    public void ApplyHeaders(IDictionary<string, StringValues> headers) {
        headers[KnownHeaders.Allow] = Allow;
    }

    public static MethodNotAllowed<T> FromResponse(
        object? body, IReadOnlyDictionary<string, string> headers) =>
        new(ResponseExpectation.Body<T>(body),
            ResponseExpectation.RequiredHeader(headers, KnownHeaders.Allow));
}
