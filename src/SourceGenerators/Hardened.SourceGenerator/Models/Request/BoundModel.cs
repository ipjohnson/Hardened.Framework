namespace Hardened.SourceGenerator.Models.Request;

/// <summary>
/// A <c>[FromForm]</c> or <c>[FromQueryString]</c> parameter whose type is a model, bound one
/// member at a time from the fields the request carried.
/// </summary>
/// <remarks>
/// <para>
/// Read where the parameter's symbol is, in the syntax transform, and carried to the output stage
/// the way <see cref="RequestParameterInformation.IsRawBody"/> is: the binder and the document
/// writer both need the members, and neither has a symbol to read them from.
/// </para>
/// <para>
/// A parameter whose type binds as one value - a string, a number, an enum, a collection of those,
/// or anything with a static <c>Parse</c> or <c>TryParse</c> - carries no model and binds exactly as
/// it did before this existed.
/// </para>
/// </remarks>
public sealed class BoundModel : IEquatable<BoundModel>
{
    public BoundModel(IReadOnlyList<BoundMember> members, string? problem, HandlerSchema? schema)
    {
        Members = members;
        Problem = problem;
        Schema = schema;
    }

    /// <summary>
    /// Every member the binder assigns, constructor arguments first and in constructor order.
    /// </summary>
    public IReadOnlyList<BoundMember> Members { get; }

    /// <summary>
    /// Why the type cannot be bound member by member, or null when it can.
    /// </summary>
    /// <remarks>
    /// Carried rather than reported because a syntax transform cannot report a diagnostic, and
    /// reported as <c>HRDW007</c> once the handler is emitted. A model with a problem binds the way
    /// it did before, as one value, so the generated code still compiles beside the error.
    /// </remarks>
    public string? Problem { get; }

    /// <summary>
    /// The model's schema, for the request body a form model publishes. Null for a query string
    /// model, which is published as one parameter per member instead.
    /// </summary>
    public HandlerSchema? Schema { get; }

    public bool Equals(BoundModel? other) =>
        other is not null
        && Members.SequenceEqual(other.Members)
        && Problem == other.Problem
        && Equals(Schema, other.Schema);

    public override bool Equals(object? obj) => Equals(obj as BoundModel);

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = Members.Count;

            foreach (var member in Members)
            {
                hashCode = (hashCode * 397) ^ member.GetHashCode();
            }

            return (hashCode * 397) ^ (Problem?.GetHashCode() ?? 0);
        }
    }
}

/// <summary>
/// One member of a <see cref="BoundModel"/>, and how the binder assigns it.
/// </summary>
/// <remarks>
/// The value is described as a <see cref="RequestParameterInformation"/> because the binder and
/// the document writer already know what to do with one: <see cref="RequestParameterInformation.Name"/>
/// is the C# member, <see cref="RequestParameterInformation.BindingName"/> the field on the wire,
/// and the type, required-ness and default drive the same conversion call a parameter gets.
/// </remarks>
public sealed class BoundMember : IEquatable<BoundMember>
{
    public BoundMember(RequestParameterInformation value, BoundMemberKind kind)
    {
        Value = value;
        Kind = kind;
    }

    public RequestParameterInformation Value { get; }

    public BoundMemberKind Kind { get; }

    public bool Equals(BoundMember? other) =>
        other is not null && Kind == other.Kind && Value.Equals(other.Value);

    public override bool Equals(object? obj) => Equals(obj as BoundMember);

    public override int GetHashCode() => (Value.GetHashCode() * 397) ^ (int)Kind;
}

/// <summary>How the binder gives a member its value.</summary>
public enum BoundMemberKind
{
    /// <summary>An argument to the constructor the binder calls.</summary>
    ConstructorArgument,

    /// <summary>
    /// An <c>init</c> or <c>required</c> property, which can only be set in the object initializer.
    /// </summary>
    Initializer,

    /// <summary>A property with a setter, assigned after construction.</summary>
    Assigned,

    /// <summary>
    /// A property with a setter and an initializer, assigned only when the request carried the
    /// field, so an absent field leaves the initializer's value in place.
    /// </summary>
    AssignedWhenSent,
}
