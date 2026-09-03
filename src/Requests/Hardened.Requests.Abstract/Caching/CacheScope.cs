namespace Hardened.Requests.Abstract.Caching;

/// <summary>
/// Who a stored response may be served to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stated by the author, because nothing else can know.</b> The filter used to decide this from
/// <c>IExecutionRequestHandlerInfo.Requirement.RequiresContext</c>, which is true only for a
/// requirement built from <c>Requirement.Predicate</c> - so it covered a shape almost nobody
/// writes and missed the two everybody does. An owner-scoped read whose ownership check is handler
/// code answering 404, which is what a description forces anyway because it can say "the caller
/// must be authenticated" and cannot say "and the row must be theirs", was cached and served to the
/// next subscriber. Three trial arms hit that independently.
/// </para>
/// <para>
/// No property of the handler distinguishes the two cases. "Every caller holding <c>rates:read</c>
/// gets these same bytes" and "each caller gets their own" are both authenticated reads with a
/// grant requirement, and the difference lives in what the handler does with the caller - which is
/// not on the metadata and cannot be inferred from it. So a handler that requires anything of its
/// caller has to say, and one that requires nothing is <see cref="AllCallers"/> as it always was.
/// </para>
/// <para>
/// Silently defaulting to <see cref="PerCaller"/> was the alternative, and it is worse than
/// failing: it is safe, and it turns one shared entry into one per caller, so a cache somebody
/// added to shed load quietly stops shedding it and grows in proportion to the caller count
/// instead. A wrong answer that looks like the right one is what this framework raises rather than
/// guesses.
/// </para>
/// </remarks>
public enum CacheScope {

    /// <summary>
    /// The declaration did not say.
    /// </summary>
    /// <remarks>
    /// The default, and a failure naming the handler on any handler that requires something of its
    /// caller. Distinguished from <see cref="AllCallers"/> rather than merged with it so that "the
    /// author did not say" and "the author said everyone" stay different answers - the same reason
    /// <c>Duration</c> keeps 0 apart from 60.
    /// </remarks>
    Unstated = 0,

    /// <summary>
    /// One entry, served to whoever the guard admits.
    /// </summary>
    /// <remarks>
    /// What a public read wants, and what an authorized read wants when the answer does not depend
    /// on who asked. The guard still runs on every request - it sits ahead of the cache and its
    /// refusals are read there - so this shares a representation among permitted callers rather
    /// than admitting anyone.
    /// </remarks>
    AllCallers,

    /// <summary>
    /// One entry per caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller's issuer and subject go into the key, so one caller's answer can never be handed
    /// to another however the handler decided what to put in it. This is what makes an owner-scoped
    /// read cacheable at all: the ownership check does not run on a hit, and with this it does not
    /// need to, because the entry belongs to the caller it was filled for.
    /// </para>
    /// <para>
    /// A caller with no subject is not cached either way. There is nothing to key on, and the entry
    /// that would result is the shared one this exists to avoid.
    /// </para>
    /// </remarks>
    PerCaller
}
