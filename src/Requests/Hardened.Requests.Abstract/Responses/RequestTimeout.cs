namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The request did not finish arriving in the time the service will wait - 408.
/// </summary>
/// <remarks>
/// <para>
/// About the request rather than about the work: the caller opened a connection and stopped
/// sending, so there was never a complete request to act on. A handler that ran and took too long
/// answers <c>504</c> if it was waiting on something else and <c>503</c> if it shed the load, and
/// neither of those is this.
/// </para>
/// <para>
/// RFC 9110 says a server sending 408 should close the connection, which is the host's decision
/// rather than this type's - a response type states the status and the body, and the transport is
/// not its to end.
/// </para>
/// </remarks>
[HttpStatus(408)]
public sealed record RequestTimeout(string? Detail = null) : IHttpStatusResponse {

    public string Type => ProblemTypes.RequestTimeout;

    public string Title => "Request Timeout";

    public int Status => 408;
}
