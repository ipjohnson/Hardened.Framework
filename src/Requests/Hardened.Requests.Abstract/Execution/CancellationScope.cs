namespace Hardened.Requests.Abstract.Execution;

/// <summary>
/// Runs a span of the pipeline on a different cancellation token, and puts the previous one back.
///
/// <code>
/// using var deadline = context.WithCancellation(cts.Token);
///
/// await chain.Next();
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>The restore is what makes the span a span.</b> Two filters read
/// <see cref="IExecutionContext.CancellationToken"/> <em>after</em> the inner chain has returned:
/// <c>ConditionalGetFilter</c> flushes the body it held back, and <c>ResponseCacheFilter</c>
/// copies its buffer to the transport and stores the entry. Leave a deadline token in place and a
/// request that spent its whole budget in the handler gets its answer flushed and its cache entry
/// written on an already-cancelled token.
/// </para>
/// <para>
/// A struct, so nothing allocates on a path every request takes. It holds the context rather than
/// a callback for the same reason.
/// </para>
/// </remarks>
public readonly struct CancellationScope : IDisposable {
    private readonly IExecutionContext _context;
    private readonly CancellationToken _previous;

    internal CancellationScope(IExecutionContext context, CancellationToken replacement) {
        _context = context;
        _previous = context.CancellationToken;
        context.CancellationToken = replacement;
    }

    public void Dispose() => _context.CancellationToken = _previous;
}

/// <summary>
/// Opening a <see cref="CancellationScope"/> on a context.
/// </summary>
public static class ExecutionContextCancellationExtensions {

    /// <summary>
    /// Makes <paramref name="replacement"/> what <see cref="IExecutionContext.CancellationToken"/>
    /// returns until the scope is disposed.
    /// </summary>
    /// <remarks>
    /// The setter stays public and this is built on it, so a test that wants to drive a request on
    /// a cancelled token can assign one. Anything wrapping a span of the pipeline should use this
    /// instead: a hand-written <c>finally</c> is one forgotten line away from running the rest of
    /// a request on a dead token.
    /// </remarks>
    public static CancellationScope WithCancellation(
        this IExecutionContext context, CancellationToken replacement) =>
        new(context, replacement);
}
