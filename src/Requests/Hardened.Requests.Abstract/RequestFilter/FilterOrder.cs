namespace Hardened.Requests.Abstract.RequestFilter;

/// <summary>
/// Where each stage of the request pipeline runs, and how to sit between two of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gaps are the feature.</b> These were consecutive integers, which left an application
/// nowhere to put a filter of its own: the natural spelling of "just after authentication" is
/// <c>FilterOrder.Authentication + 100</c>, and on the old scale that was 102, landing past
/// serialization, validation, resource authorization and the retry filter. It compiled, it ran, and
/// it meant close to the opposite of what was written. A thousand between stages makes that
/// expression true, and <see cref="Before"/> and <see cref="After"/> make it readable.
/// </para>
/// <para>
/// <b>Every value is a relationship rather than a literal.</b> One anchor at
/// <see cref="Serialization"/> and each stage a gap from its neighbour, so the reason a stage sits
/// where it does stays in the file instead of in whoever last renumbered it.
/// </para>
/// <para>
/// <b>They are <c>const</c>, so a consumer inlines them.</b> An assembly compiled against one set
/// of values keeps using those values until it is rebuilt, and a filter at a stale position is a
/// silent behaviour change rather than an error. That is why the scale is wide enough never to move
/// again rather than only wide enough for what is here now.
/// </para>
/// <para>
/// Everything ahead of <see cref="Serialization"/> can refuse a request before its body has been
/// read, and the stages there are in cheapest-refusal-first order. A filter on that side of the
/// line refuses by recording the failure and continuing, so the serialization filter can write the
/// response; behind it, an ordinary short circuit is what stops the handler.
/// </para>
/// <para>
/// <b>What a handler's chain was composed into is written once, as it is built</b>, when the
/// <c>Hardened.Requests.Pipeline</c> log category is enabled at Debug: each filter and its order,
/// in the order they run. A position that reads right and lands wrong shows there rather than in
/// a stack trace, and an application that has not enabled it pays nothing per request.
/// </para>
/// </remarks>
public static class FilterOrder {

    /// <summary>The distance between two stages.</summary>
    private const int Gap = 1000;

    /// <summary>
    /// Half a gap earlier: a position between two stages, ahead of the named one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Before + Serialization</c> and <c>After + ResponseCache</c> are the same position, which
    /// is what both of them mean.
    /// </para>
    /// <para>
    /// <b>It does not compose.</b> <c>Before + Before + Serialization</c> is a whole gap and lands
    /// exactly on <see cref="ResponseCache"/>. Name the earlier stage instead.
    /// </para>
    /// </remarks>
    public const int Before = -(Gap / 2);

    /// <summary>
    /// Half a gap later: a position between two stages, behind the named one. See
    /// <see cref="Before"/>, including for why it does not compose.
    /// </summary>
    public const int After = Gap / 2;

    /// <summary>
    /// Creating the handler instance - the outermost position there is.
    /// </summary>
    /// <remarks>
    /// Before <see cref="Authentication"/>, which reads the handler's metadata for the schemes the
    /// operation accepts and so needs a handler to read it from. Far outside the pipeline band
    /// rather than one gap from it, so a filter that has to wrap even this has somewhere to go.
    /// </remarks>
    public const int HandlerCreation = -(Gap * 10);

    /// <summary>
    /// Refusing a request on volume, before anyone has asked who is making it.
    /// </summary>
    /// <remarks>
    /// Ahead of <see cref="Authentication"/>, and that is the whole reason this position is
    /// separate from <see cref="RateLimitPrincipal"/>: a limiter meant to blunt a credential-stuffing
    /// flood cannot wait for the credential to be examined, because examining it is the work being
    /// flooded. It keys on whatever identifies the transport, since there is nothing else to key on
    /// this early.
    /// </remarks>
    public const int RateLimitTransport = Authentication - Gap;

    /// <summary>
    /// Establishing who the caller is.
    /// </summary>
    /// <remarks>
    /// Before <see cref="Serialization"/>, and deliberately: a request carrying no credential must
    /// not cause a 10 MB body to be deserialized before it is rejected. Authentication needs nothing
    /// but headers, so nothing is lost by settling it this early.
    /// </remarks>
    public const int Authentication = RateLimitPrincipal - Gap;

    /// <summary>
    /// Refusing a request on volume, once it is known whose volume it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After <see cref="Authentication"/> for the obvious reason: a limit keyed on the caller needs
    /// a caller.
    /// </para>
    /// <para>
    /// Ahead of <see cref="GrantAuthorization"/>, because resolving a caller's grants can reach a
    /// permissions store, and bounding work like that is what a limiter is for. The cost is that a
    /// caller who would have been refused anyway still spends a permit, which is the right way
    /// round: a caller sending requests they hold no grant for is the case worth limiting.
    /// </para>
    /// <para>
    /// This sat behind <see cref="Retry"/> until the scale was widened, not by choice but because
    /// the integers either side of serialization were taken. The consequence was that a limiter
    /// keyed on the caller read the whole request body before refusing it, which is most of what a
    /// limiter exists to avoid.
    /// </para>
    /// </remarks>
    public const int RateLimitPrincipal = GrantAuthorization - Gap;

    /// <summary>
    /// Deciding whether the caller may proceed, from grants alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A requirement naming only grants can be settled from the caller, so it is settled here,
    /// before a body is read. One that reads bound parameters cannot and waits for
    /// <see cref="Authorization"/>. Which of the two applies is decided from the requirement tree
    /// once per handler, not per request.
    /// </para>
    /// <para>
    /// This is a stage rather than <c>Authentication + 1</c>, which is how
    /// <c>AuthorizationFilterProvider</c> used to spell it. A position documented in three files and
    /// expressed nowhere is one nothing can sit beside: with a name, "resolve the tenant from the
    /// token, then check grants" has somewhere to go.
    /// </para>
    /// </remarks>
    public const int GrantAuthorization = Conditional - Gap;

    /// <summary>
    /// Answering a conditional request from a validator the caller already holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Occupied by <c>ConditionalGetFilter</c>, which <c>[ConditionalGet]</c> installs on an
    /// operation or a class and <c>[Enable&lt;ConditionalGet&gt;]</c> on every GET handler: a
    /// request carrying <c>If-None-Match</c> or <c>If-Modified-Since</c> is answered 304 when the
    /// response it would otherwise have been given carries the validator it names. The response
    /// cache tags every entry it stores, static content hashes what it serves, the filter tags
    /// what it sends when the handler wrote no validator of its own, and a handler that knows its
    /// resource's version writes <c>ETag</c> or <c>Last-Modified</c> itself. Nothing installs the
    /// filter without a declaration. The rule for what matches is <c>Precondition</c>.
    /// </para>
    /// <para>
    /// Ahead of <see cref="ResponseCache"/>, and that ordering is the design rather than a
    /// preference. A 304 costs no body at all, so it is the cheaper answer and belongs first; and a
    /// validator filter placed inside the cache would never run on a hit, so a cached response could
    /// never be revalidated. Outside compression for the same reason: a 304 has nothing to encode,
    /// and the filter that would have encoded it sits inside this stage, so what it wrote is
    /// dropped with the body.
    /// </para>
    /// </remarks>
    public const int Conditional = ResponseCache - Gap;

    /// <summary>
    /// Serving a stored response instead of running the handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its position is a correctness requirement rather than a convenience, which is what earns it
    /// a stage instead of an offset. Ahead of <see cref="Serialization"/>, so a hit skips the bind,
    /// the handler and the serialize rather than only the handler. Behind
    /// <see cref="GrantAuthorization"/> and ahead of <see cref="Authorization"/>, which is why a
    /// handler guarded by a requirement over bound parameters is never cached: a stored answer to
    /// "may this caller edit <em>this</em> pet" served to a second caller is a data leak.
    /// </para>
    /// <para>
    /// <c>[CacheControl]</c> sits at <see cref="BeforeSerialization"/> instead, one half-gap behind,
    /// so it runs inside the cache. On a hit it does not run at all and the header it wrote on the
    /// miss is replayed with the rest of them.
    /// </para>
    /// </remarks>
    public const int ResponseCache = Serialization - Gap;

    /// <summary>
    /// The ordinary slot for work that has to happen just before the response is serialized.
    /// </summary>
    /// <remarks>
    /// Not a stage. It is the position <c>Before + Serialization</c> names, kept as a constant
    /// because it is a common one and because <c>[CacheControl]</c> and Hardened.Amz both reference
    /// it. Anything else wanting to sit between two stages writes the expression.
    /// </remarks>
    public const int BeforeSerialization = Before + Serialization;

    /// <summary>
    /// Binding the request on the way in, and serializing the response on the way out.
    /// </summary>
    /// <remarks>
    /// The anchor every other stage is measured from, and the line either side of which a filter
    /// refuses differently. See the remarks on <see cref="FilterOrder"/>.
    /// </remarks>
    public const int Serialization = Gap * 7;

    /// <summary>
    /// Checking the constraints the contract declared, over the parameters that were just bound.
    /// </summary>
    public const int Validation = Serialization + Gap;

    /// <summary>
    /// Deciding whether the caller may proceed, from the resource as well as the caller.
    /// </summary>
    /// <remarks>
    /// After <see cref="Validation"/>, because a requirement over the resource - "may this caller
    /// edit <em>this</em> pet" - reads bound parameters, and they do not exist until then. A
    /// requirement over grants alone does not wait: it runs at <see cref="GrantAuthorization"/>.
    /// </remarks>
    public const int Authorization = Validation + Gap;

    /// <summary>
    /// Re-running the handler after a failure.
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
    public const int Retry = Authorization + Gap;

    /// <summary>
    /// Where a filter that states no order lands: after every stage, before the handler.
    /// </summary>
    /// <remarks>
    /// Far above <see cref="Retry"/> rather than one gap from it, so stages can still be added
    /// without this moving. Moving it would be worse than moving a stage, because it is the default
    /// parameter value on <c>IGlobalFilterRegistry.RegisterFilter</c> and those are inlined at every
    /// call site.
    /// </remarks>
    public const int DefaultValue = Gap * 100;

    public const int EndPointHandlers = DefaultValue * 2;

    /// <summary>
    /// The handler. Terminal: it never calls <c>Next</c>, so a filter ordered above this is built
    /// into the chain and never reached.
    /// </summary>
    public const int EndPointInvoke = DefaultValue * 2;
}
