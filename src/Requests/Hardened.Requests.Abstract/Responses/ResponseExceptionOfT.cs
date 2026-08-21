namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A response type, thrown, with the case type still visible to a <c>catch</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ResponseException"/> already carries any response to the wire. What this adds is one
/// thing: <c>catch (ResponseException&lt;NotFound&gt;)</c>, and a <see cref="Response"/> typed as the
/// case rather than as the interface. Without it a caller catches the base and tests
/// <c>e.Response is NotFound</c>, which the compiler cannot check and a rename does not reach.
/// </para>
/// <para>
/// <b>It is possible at all because a case type is a plain record.</b> Had the built-in responses
/// derived from <c>Exception</c> - the shape a generated <c>GetPetNotFoundException</c> takes on the
/// specification-first throw path - a type could be a thrown error or a union case and not both.
/// Keeping them plain is what lets one <c>NotFound</c> serve as a declared case, a returned value,
/// and the payload of a thrown exception.
/// </para>
/// <para>
/// Derived from the non-generic form, so <c>catch (ResponseException)</c> still catches every one of
/// these and <c>ExceptionToModelConverter</c> needs no knowledge of it: the converter matches
/// <c>IStatusCodeException</c> and reads the body off <c>StatusCodeException.Value</c>, both
/// inherited.
/// </para>
/// <para>
/// <b>A thrown response is not a declared one.</b> It is not in the return type, so the response-set
/// diagnostics cannot see it and the OpenAPI document does not list it. That is deliberate - the
/// union is the governed channel and <c>throw</c> is the ungoverned one, which is what lets an
/// organisation put authorization decisions in filters - but it means throwing a <c>NotFound</c> and
/// declaring one are not the same statement about the contract.
/// </para>
/// </remarks>
/// <typeparam name="T">The case type being thrown.</typeparam>
public class ResponseException<T> : ResponseException where T : IHttpStatusResponse {

    public ResponseException(T response, string? message = null)
        : base(response, message) {
        Response = response;
    }

    /// <summary>The response this was thrown for, as the case type.</summary>
    /// <remarks>
    /// Held rather than cast from the base on each read, and <c>new</c> rather than an override
    /// because the base property is not virtual - a caller holding the base type still gets the
    /// same instance through it, so the two never disagree about what was thrown.
    /// </remarks>
    public new T Response { get; }
}

/// <summary>
/// Turns a response into the exception that carries it.
/// </summary>
public static class ResponseExceptionExtensions {

    /// <summary>
    /// <paramref name="response"/> as a thrown exception, keeping its type.
    /// </summary>
    /// <remarks>
    /// An extension so the case type is inferred: <c>throw new NotFound("todo").AsException();</c>
    /// rather than naming <c>NotFound</c> twice. The alternative reads as a declaration of the type
    /// followed by a repetition of it, which is exactly the noise the built-in types exist to remove.
    /// </remarks>
    public static ResponseException<T> AsException<T>(this T response, string? message = null)
        where T : IHttpStatusResponse =>
        new(response, message);
}
