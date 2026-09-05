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
/// </remarks>
internal sealed class RequestDeadline : IRequestDeadline {

    private static readonly AsyncLocal<MachineTimestamp?> Current = new();

    /// <inheritdoc />
    public MachineTimestamp? Deadline => Current.Value;

    /// <summary>
    /// Publishes <paramref name="deadline"/> until the scope is disposed, or nothing when it is
    /// null.
    /// </summary>
    internal static DeadlineScope Until(MachineTimestamp? deadline) => new(deadline);

    /// <summary>The value the scope reads and restores. Not part of the contract.</summary>
    internal static MachineTimestamp? Value {
        get => Current.Value;
        set => Current.Value = value;
    }
}

/// <summary>
/// Publishes a deadline for a span of the pipeline, and puts the previous one back.
/// </summary>
/// <remarks>
/// <para>
/// The restore is what makes nested budgets nest: an inner <c>[Timeout]</c> is what a handler
/// inside it reads, and the outer one is what is visible again afterwards. It matters for the same
/// reason <see cref="Hardened.Requests.Abstract.Execution.CancellationScope"/>'s does, and is
/// written the same way.
/// </para>
/// <para>
/// A struct, so a bounded request allocates nothing for this. Constructing one with null writes
/// nothing and disposing it restores nothing, which is how a handler that declined the deadline
/// pays for none of it.
/// </para>
/// </remarks>
internal readonly struct DeadlineScope : IDisposable {
    private readonly MachineTimestamp? _previous;
    private readonly bool _published;

    internal DeadlineScope(MachineTimestamp? deadline) {
        if (deadline is not { } until) {
            _previous = null;
            _published = false;

            return;
        }

        _previous = RequestDeadline.Value;
        _published = true;
        RequestDeadline.Value = until;
    }

    public void Dispose() {
        if (_published) {
            RequestDeadline.Value = _previous;
        }
    }
}
