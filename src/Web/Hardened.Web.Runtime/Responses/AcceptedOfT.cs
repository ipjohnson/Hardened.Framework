using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Responses;

/// <summary>
/// The work was taken and has not been done yet - 202, a representation of the accepted work, and
/// where to watch it if there is anywhere.
/// </summary>
/// <remarks>
/// <para>
/// For a 202 whose body describes the job rather than its result: an identifier, a state, an
/// estimate. <see cref="Accepted"/> is the bodyless form, and the one to reach for when there is
/// nothing to say beyond the address.
/// </para>
/// <para>
/// <b>The body is <see cref="Value"/>, not this record</b>, through
/// <see cref="ICarriesResponseBody"/>, for the reason <see cref="Created{T}"/> gives.
/// <see cref="Location"/> is optional, for the reason <see cref="Accepted"/> gives.
/// </para>
/// </remarks>
[HttpStatus(202)]
public sealed record Accepted<T>(T Value, string? Location = null)
    : IHttpStatusResponse,
        IProvidesResponseHeaders,
        ICarriesResponseBody,
        IResponseExpectation<Accepted<T>>
{
    public static int StatusCode => 202;

    public int Status => StatusCode;

    object? ICarriesResponseBody.Body => Value;

    public void ApplyHeaders(IDictionary<string, StringValues> headers)
    {
        if (!string.IsNullOrEmpty(Location))
        {
            headers[KnownHeaders.Location] = Location!;
        }
    }

    /// <summary>
    /// Every header the response carried, where <see cref="FromResponse"/> built this from a call.
    /// Empty where a handler built it, and never sent.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers => ResponseExpectation.HeadersOf(this);

    public static Accepted<T> FromResponse(
        object? body,
        IReadOnlyDictionary<string, string> headers
    ) =>
        ResponseExpectation.Received<Accepted<T>>(
            new(
                ResponseExpectation.Body<T>(body),
                ResponseExpectation.OptionalHeader(headers, KnownHeaders.Location)
            ),
            headers
        );
}
