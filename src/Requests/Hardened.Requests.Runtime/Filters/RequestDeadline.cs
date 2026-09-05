using Hardened.Requests.Abstract.Timeouts;
using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// The singleton <see cref="IRequestDeadline"/> resolves to, reading the request it is asked on.
/// </summary>
/// <remarks>
/// <para>
/// Written by <see cref="TimeoutFilter"/> for the span of the chain it bounds, and read by whatever
/// the handler was resolved with. A static <c>AsyncLocal</c> rather than a scoped holder, because a
/// described handler can only take this through its constructor and may be a singleton, which a
/// scoped registration would let it capture.
/// </para>
/// <para>
/// <b>The write is the cost.</b> Setting an <c>AsyncLocal</c> makes the execution context
/// non-default for the rest of the request, so every continuation after it copies one. That is why
/// nothing writes this unless a budget was declared, and why <c>[Timeout(Deadline = false)]</c>
/// exists for a bounded handler that would rather not pay it.
/// </para>
/// <para>
/// One <c>AsyncLocal</c> holding both readings rather than two. They describe one fact and are
/// published together, and two cells could be read a continuation apart and disagree.
/// </para>
/// </remarks>
internal sealed class RequestDeadline : IRequestDeadline {

    private static readonly AsyncLocal<Bound?> Current = new();

    /// <inheritdoc />
    public MachineTimestamp? Deadline => Current.Value?.Deadline;

    /// <inheritdoc />
    public CancellationToken CancellationToken =>
        Current.Value?.CancellationToken ?? CancellationToken.None;

    /// <summary>
    /// Publishes a budget until the scope is disposed, or nothing when <paramref name="deadline"/>
    /// is null.
    /// </summary>
    internal static DeadlineScope Until(MachineTimestamp? deadline, CancellationToken token) =>
        new(deadline, token);

    /// <summary>The cell the scope reads and restores. Not part of the contract.</summary>
    internal static Bound? Value {
        get => Current.Value;
        set => Current.Value = value;
    }

    /// <summary>What a bounded request has: when it runs out, and the token that fires then.</summary>
    internal sealed record Bound(MachineTimestamp Deadline, CancellationToken CancellationToken);
}

/// <summary>
/// Publishes a budget for a span of the pipeline, and puts the previous one back.
/// </summary>
/// <remarks>
/// <para>
/// The restore is what makes nested budgets nest: an inner <c>[Timeout]</c> is what a handler
/// inside it reads, and the outer one is what is visible again afterwards. It matters for the same
/// reason <see cref="Hardened.Requests.Abstract.Execution.CancellationScope"/>'s does, and is
/// written the same way.
/// </para>
/// <para>
/// A struct, so the scope itself allocates nothing. Constructing one with a null deadline writes
/// nothing and disposing it restores nothing, which is how a handler that declined the budget pays
/// for none of it.
/// </para>
/// </remarks>
internal readonly struct DeadlineScope : IDisposable {
    private readonly RequestDeadline.Bound? _previous;
    private readonly bool _published;

    internal DeadlineScope(MachineTimestamp? deadline, CancellationToken token) {
        if (deadline is not { } until) {
            _previous = null;
            _published = false;

            return;
        }

        _previous = RequestDeadline.Value;
        _published = true;
        RequestDeadline.Value = new RequestDeadline.Bound(until, token);
    }

    public void Dispose() {
        if (_published) {
            RequestDeadline.Value = _previous;
        }
    }
}
