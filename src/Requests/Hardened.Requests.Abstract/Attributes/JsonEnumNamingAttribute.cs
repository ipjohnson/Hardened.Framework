namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// The vocabulary an enum is written and read as.
/// </summary>
/// <remarks>
/// Names for the wire, not for C#. The member name is a C# identifier and the wire value is part of
/// an API's contract, and they are only the same thing by coincidence.
/// </remarks>
public enum EnumNaming {

    /// <summary>The C# member name, unchanged - <c>InProgress</c>.</summary>
    /// <remarks>
    /// What System.Text.Json's own <c>UseStringEnumConverter</c> produces, and what a code-first
    /// application wrote before this attribute existed. Set it explicitly on an enum whose values
    /// are already the wire's.
    /// </remarks>
    MemberName,

    /// <summary><c>inProgress</c>.</summary>
    CamelCase,

    /// <summary><c>in-progress</c>.</summary>
    KebabCaseLower,

    /// <summary><c>in_progress</c>.</summary>
    SnakeCaseLower,

    /// <summary><c>IN_PROGRESS</c>.</summary>
    SnakeCaseUpper
}

/// <summary>
/// How this application's enums reach the wire.
/// </summary>
/// <remarks>
/// <para>
/// On the assembly it is the default for every enum that assembly serializes; on an enum it
/// overrides that default for one type. An application that sets neither writes
/// <see cref="EnumNaming.CamelCase"/>, which is what the property names beside it use.
/// </para>
/// <para>
/// The assembly rather than the module class, and the target list is narrow so that a misplaced one
/// is a compile error rather than a setting that reads as applied and is not. The document is
/// written inside the syntax transform, where a handler's own symbol is reachable and the module's
/// is not - and a naming the document did not get is the desynchronised contract this attribute
/// exists to prevent.
/// </para>
/// <para>
/// <b>This governs the published document as well as the wire.</b> The generated OpenAPI description
/// declares each enum's values, and it is written from this same setting - so the contract a client
/// generates against and the bytes the application actually produces cannot disagree. That was the
/// defect this replaced: the document said <c>{"type":"string","enum":["ScienceFiction"]}</c> for
/// every enum while the serializer wrote <c>0</c>.
/// </para>
/// <para>
/// It governs parameters too. A path or query value is text rather than JSON, so it never reaches a
/// JSON converter - <c>?priority=in-progress</c> would be answered 400 by an application whose body
/// accepts exactly that value. The generated binder is given the same vocabulary.
/// </para>
/// <para>
/// Contract-first applications do not use this. Their enum values come from the description, which
/// is the vocabulary by definition, and the build already emits converters carrying it.
/// </para>
/// <example>
/// <code>
/// // AssemblyInfo.cs, or any file in the project
/// [assembly: JsonEnumNaming(EnumNaming.KebabCaseLower)]
///
/// public enum Priority { Low, InProgress }            // "low", "in-progress"
///
/// [JsonEnumNaming(EnumNaming.MemberName)]
/// public enum LegacyCode { AB12, CD34 }               // "AB12", "CD34"
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Enum, AllowMultiple = false)]
public class JsonEnumNamingAttribute : Attribute {

    public JsonEnumNamingAttribute(EnumNaming naming) {
        Naming = naming;
    }

    public EnumNaming Naming { get; }
}
