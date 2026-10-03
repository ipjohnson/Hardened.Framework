namespace Hardened.Requests.Abstract.Execution;

/// <summary>Which of the three things a change feed reports happened to a row.</summary>
public enum ChangeKind
{
    /// <summary>The row was created, so there is no row before.</summary>
    Insert,

    /// <summary>The row was replaced, so there is a row before and a row after.</summary>
    Modify,

    /// <summary>The row was deleted, so there is no row after.</summary>
    Remove,
}

/// <summary>
/// One change to a row, as a test sends it through a <c>Changes</c> façade.
/// </summary>
/// <remarks>
/// <para>
/// A plain message sent to a change façade arrives as a modify whose row before and row after are
/// both the message. A handler that compares the two cannot be tested that way, so a test sends
/// one of these instead: <c>changes.Orders(Transition.Modify(before, after))</c>.
/// </para>
/// <para>
/// A delivery reads <see cref="Before"/>, <see cref="After"/> and <see cref="Kind"/> off this base
/// class, so it handles every payload type without knowing any of them.
/// </para>
/// </remarks>
public abstract class Transition
{
    private protected Transition(ChangeKind kind, object? before, object? after)
    {
        Kind = kind;
        Before = before;
        After = after;
    }

    public ChangeKind Kind { get; }

    /// <summary>The row before the change, or null for an insert.</summary>
    public object? Before { get; }

    /// <summary>The row after the change, or null for a remove.</summary>
    public object? After { get; }

    /// <summary>The row the handler binds: the row after, or the row before for a remove.</summary>
    public object Row => (After ?? Before)!;

    public static Transition<T> Insert<T>(T after) => new(ChangeKind.Insert, null, after);

    public static Transition<T> Modify<T>(T before, T after) =>
        new(ChangeKind.Modify, before, after);

    public static Transition<T> Remove<T>(T before) => new(ChangeKind.Remove, before, null);
}

/// <summary>
/// A <see cref="Transition"/> of one payload type, which is what the façade method takes.
/// </summary>
public sealed class Transition<T> : Transition
{
    internal Transition(ChangeKind kind, object? before, object? after)
        : base(kind, before, after) { }
}
