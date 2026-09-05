namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The service failed and the request was not at fault - 500.
/// </summary>
/// <remarks>
/// <para>
/// <b>Declared rather than thrown.</b> The pipeline already answers 500 for an unhandled
/// exception, and it deliberately says nothing about it - the message was written for whoever
/// reads the logs. This is the other 500: the one a description declares, that a handler answers
/// on purpose, with a body the contract promised.
/// </para>
/// <para>
/// Near-universal in a description and absent from the shipped set until now, so it was the one
/// common status a specification-first build had to generate a type for. <see cref="Detail"/> is
/// still the service's to choose, and the same caution applies as anywhere else: a 500's detail is
/// read by the caller, not by the operator.
/// </para>
/// </remarks>
[HttpStatus(500)]
public sealed record InternalServerError(string? Detail = null)
    : IHttpStatusResponse, IDeclaresStatus {

    /// <summary>
    /// The InternalServerError with a generic message, for a handler with nothing more to say than the status.
    /// Shared, so returning it allocates nothing.
    /// </summary>
    public static readonly InternalServerError Default = new("An unexpected error occurred.");

    public string Type => ProblemTypes.InternalServerError;

    public string Title => "Internal Server Error";

    public static int StatusCode => 500;

    public int Status => StatusCode;
}
