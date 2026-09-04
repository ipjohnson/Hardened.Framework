namespace Hardened.Generation.Models;

/// <summary>
/// A deadline an operation's description declares: how long it may take, and what its caller is
/// told when it does not finish.
/// </summary>
/// <remarks>
/// <para>
/// The specification-first half of <c>[Timeout]</c>. Both front ends parse into this, and the spec
/// bridge emits it back out as a <c>TimeoutAttribute</c> in the handler's metadata - which is where
/// the runtime's cascade reads a declaration from, so a budget written in a model and one written
/// on a method reach the pipeline by the same path and resolve against the same rungs.
/// </para>
/// <para>
/// Milliseconds rather than a duration string, because that is what the attribute carries and what
/// <c>CancelAfter</c> takes, and a second unit would be a second place for the two to disagree.
/// </para>
/// </remarks>
internal class TimeoutModel : IEquatable<TimeoutModel> {

    /// <summary>The budget. A model declaring zero or less is refused where it is parsed.</summary>
    public int Milliseconds { get; set; }

    /// <summary>What the caller is told, 504 unless the description says otherwise.</summary>
    public int Status { get; set; } = 504;

    /// <summary>Seconds for <c>Retry-After</c>, or zero for no header.</summary>
    public int RetryAfterSeconds { get; set; }

    public bool Equals(TimeoutModel? other) =>
        other is not null &&
        Milliseconds == other.Milliseconds &&
        Status == other.Status &&
        RetryAfterSeconds == other.RetryAfterSeconds;

    public override bool Equals(object? obj) => Equals(obj as TimeoutModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Milliseconds;

            hash = (hash * 397) ^ Status;
            hash = (hash * 397) ^ RetryAfterSeconds;

            return hash;
        }
    }
}
