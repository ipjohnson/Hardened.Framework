namespace Hardened.Requests.Abstract.RequestFilter;

public static class FilterOrder {
    public const int HandlerCreation = -1000;

    /// <summary>
    /// Refusing a request on volume, before anyone has asked who is making it - 1.
    /// </summary>
    /// <remarks>
    /// Ahead of <see cref="Authentication"/>, because a limiter meant to blunt a credential-stuffing
    /// flood cannot wait for the credential to be examined, and ahead of
    /// <see cref="Serialization"/> for the reason <see cref="Authentication"/> is: a request about
    /// to be refused must not cost a 10 MB deserialization first. Being on that side of the line
    /// means a filter here refuses by recording the failure and continuing, not by returning.
    /// </remarks>
    public const int RateLimitTransport = Authentication - 1;

    /// <summary>
    /// Establishing who the caller is - 2.
    /// </summary>
    /// <remarks>
    /// Before <see cref="BeforeSerialization"/>, and deliberately: a request carrying no credential
    /// must not cause a 10 MB body to be deserialized before it is rejected. Authentication needs
    /// nothing but headers, so nothing is lost by settling it this early.
    ///
    /// Two below rather than one, because a requirement over grants alone is authorized at
    /// <c>Authentication + 1</c> and that slot must still land before serialization.
    /// </remarks>
    public const int Authentication = BeforeSerialization - 2;

    public const int BeforeSerialization = Serialization - 1;

    public const int Serialization = 5;

    public const int Validation = Serialization + 1;

    /// <summary>
    /// Deciding whether the caller may proceed - 7.
    /// </summary>
    /// <remarks>
    /// After <see cref="Validation"/>, because a requirement over the resource - "may this caller
    /// edit <em>this</em> pet" - reads bound parameters, and they do not exist until then. A
    /// requirement over grants alone does not wait: it runs at <see cref="Authentication"/> + 1.
    /// Which of the two applies is decided from the requirement tree once per handler, not per
    /// request.
    /// </remarks>
    public const int Authorization = Validation + 1;

    /// <summary>
    /// Re-running the handler after a failure - 9.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After <see cref="Serialization"/>, and that is not a preference. The filter at that position
    /// catches whatever the handler failed with, records it on the response and returns normally -
    /// so a retry filter ordered <em>ahead</em> of it is handed a request that looks like it
    /// succeeded, and stops after one attempt. Ordered behind it, the failure is still on the
    /// response when the retry filter looks, and the response is serialized once when the retry
    /// filter is finally done rather than once per attempt.
    /// </para>
    /// <para>
    /// After <see cref="Authorization"/> as well, because a refusal is not transient. Retrying one
    /// spends the whole budget re-deriving the same answer.
    /// </para>
    /// <para>
    /// The cost of the position is that the handler instance is created once, at
    /// <see cref="HandlerCreation"/>, and every attempt shares it. A handler that keeps mutable
    /// per-request state on itself is not one that can be retried.
    /// </para>
    /// </remarks>
    public const int Retry = Authorization + 1;

    /// <summary>
    /// Refusing a request on volume, once it is known whose volume it is - 9.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After <see cref="Authentication"/> for the obvious reason: a limit keyed on the caller needs
    /// a caller. There is no room for it between authentication and
    /// <see cref="Serialization"/> - 3 and 4 are taken and a tie sorts unpredictably - so it sits
    /// here, which means the body has already been read by the time it refuses. That is the cost of
    /// the position and the reason <see cref="RateLimitTransport"/> exists: a limiter that must
    /// refuse before reading a body keys on the transport instead.
    /// </para>
    /// <para>
    /// Behind <see cref="Serialization"/>, so a filter here refuses by returning rather than by
    /// recording and continuing.
    /// </para>
    /// </remarks>
    public const int RateLimitPrincipal = Retry + 1;

    public const int DefaultValue = 1000;

    public const int EndPointHandlers = DefaultValue * 2;

    public const int EndPointInvoke = DefaultValue * 2;
}