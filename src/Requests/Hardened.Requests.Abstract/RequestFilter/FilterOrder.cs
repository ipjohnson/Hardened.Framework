namespace Hardened.Requests.Abstract.RequestFilter;

public static class FilterOrder {
    public const int HandlerCreation = -1000;

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

    public const int DefaultValue = 1000;

    public const int EndPointHandlers = DefaultValue * 2;

    public const int EndPointInvoke = DefaultValue * 2;
}