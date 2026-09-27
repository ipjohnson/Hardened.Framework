namespace Hardened.SourceGenerator.Models.Request;

/// <summary>
/// A request header the operation reads that no handler parameter binds.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>RequestParameterInformation</c>, which describes a value the generated binder
/// passes to the handler. Nothing binds this one: a filter reads it before the handler runs, and
/// the only thing to say about it is that a caller may send it. Putting it in the binder's list
/// would generate an argument for a parameter that does not exist.
/// </para>
/// <para>
/// Always optional and always a string, for the reasons <c>[ReadsHeader]</c> gives.
/// </para>
/// </remarks>
public sealed class DeclaredHeaderParameterModel : System.IEquatable<DeclaredHeaderParameterModel>
{
    public DeclaredHeaderParameterModel(
        string name,
        string? description,
        string? whenAnswered = null
    )
    {
        Name = name;
        Description = description;
        WhenAnswered = whenAnswered;
    }

    /// <summary>The header's name, as it goes on the wire.</summary>
    public string Name { get; }

    /// <summary>The parameter's <c>description</c>, or null.</summary>
    public string? Description { get; }

    /// <summary>
    /// The response header the operation must declare for this parameter to be published, or null.
    /// </summary>
    public string? WhenAnswered { get; }

    public bool Equals(DeclaredHeaderParameterModel? other) =>
        other is not null
        && Name == other.Name
        && Description == other.Description
        && WhenAnswered == other.WhenAnswered;

    public override bool Equals(object? obj) => Equals(obj as DeclaredHeaderParameterModel);

    public override int GetHashCode() =>
        unchecked(
            (((Name.GetHashCode() * 397) ^ (Description?.GetHashCode() ?? 0)) * 397)
            ^ (WhenAnswered?.GetHashCode() ?? 0)
        );
}
