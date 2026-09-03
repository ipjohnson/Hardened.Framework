namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The <c>type</c> URI each built-in problem response is identified by.
/// </summary>
/// <remarks>
/// <para>
/// RFC 9457 makes <c>type</c> the identity of a problem <em>kind</em> - the member a client matches
/// on, in preference to the status, because two different problems share a status far more often
/// than they share a meaning. That is also why there is no shared problem envelope here: a single
/// type covering every status would have one <c>type</c> URI for every distinct problem, which is
/// the one thing the specification asks it not to be.
/// </para>
/// <para>
/// <b>URNs rather than <c>https://</c> URLs.</b> RFC 9457 permits any URI and tells consumers not to
/// dereference one, so the only real requirements are that it is stable and that it is ours. A URN
/// satisfies both without depending on a domain the project does not own and without a link that
/// can start answering 404 later. <see cref="Prefix"/> is the single place to change if the project
/// ever adopts a documentation URL - and changing it is a wire-visible change to every client
/// matching on <c>type</c>, so it is a decision rather than an edit.
/// </para>
/// </remarks>
public static class ProblemTypes {

    /// <summary>The namespace every built-in problem type is qualified by.</summary>
    public const string Prefix = "urn:hardened:problem:";

    public const string BadRequest = Prefix + "bad-request";

    public const string Unauthorized = Prefix + "unauthorized";

    public const string PaymentRequired = Prefix + "payment-required";

    public const string Forbidden = Prefix + "forbidden";

    public const string NotFound = Prefix + "not-found";

    public const string RequestTimeout = Prefix + "request-timeout";

    public const string Conflict = Prefix + "conflict";

    public const string Gone = Prefix + "gone";

    public const string PreconditionFailed = Prefix + "precondition-failed";

    public const string ContentTooLarge = Prefix + "content-too-large";

    public const string UnsupportedMediaType = Prefix + "unsupported-media-type";

    public const string UnprocessableContent = Prefix + "unprocessable-content";

    public const string PreconditionRequired = Prefix + "precondition-required";

    public const string RateLimited = Prefix + "rate-limited";

    public const string InternalServerError = Prefix + "internal-server-error";

    public const string NotImplemented = Prefix + "not-implemented";

    public const string BadGateway = Prefix + "bad-gateway";

    public const string ServiceUnavailable = Prefix + "service-unavailable";

    public const string GatewayTimeout = Prefix + "gateway-timeout";
}
