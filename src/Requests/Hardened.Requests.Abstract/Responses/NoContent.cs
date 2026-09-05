namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// It worked and there is nothing to send back - 204.
/// </summary>
/// <remarks>
/// <para>
/// A type rather than a <c>void</c> handler, because the two say different things in a response
/// set. <c>void</c> is the absence of a declared response; <c>NoContent</c> is a declared response
/// that happens to be empty, and only the second one appears in the document a client generates
/// from.
/// </para>
/// <para>
/// <see cref="IHttpStatusResponse.HasBody"/> is false, which becomes <c>ShouldSerialize = false</c>
/// on the execution response. That is what keeps a 204 from carrying <c>{}</c> - a body on a 204 is
/// not merely redundant, it is a response some clients and intermediaries will reject outright.
/// </para>
/// </remarks>
[HttpStatus(204)]
public sealed record NoContent : IHttpStatusResponse, IResponseExpectation<NoContent> {

    public static int StatusCode => 204;

    public int Status => StatusCode;

    public bool HasBody => false;

    public static NoContent FromResponse(
        object? body, IReadOnlyDictionary<string, string> headers) => new();
}
