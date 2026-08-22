using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// The wire vocabulary of a code-first enum, resolved once and used everywhere it shows.
/// </summary>
/// <remarks>
/// <para>
/// Four places have to agree about what an enum's values are: the JSON converter that writes them,
/// the converter that reads them back, the string converter a path or query parameter binds
/// through, and the <c>enum</c> array in the generated OpenAPI document. Any two of those
/// disagreeing is a defect no build reports - the last pairing is the one that shipped, with the
/// document declaring <c>"type":"string"</c> for enums the serializer wrote as integers.
/// </para>
/// <para>
/// So the policy is resolved here and the callers ask rather than deciding. It is not a formatting
/// helper; it is the single definition of a contract.
/// </para>
/// </remarks>
internal static class EnumWireNaming {

    private const string AttributeName = "JsonEnumNamingAttribute";
    private const string AttributeNamespace = "Hardened.Requests.Abstract.Attributes";

    /// <summary>
    /// What an application that has said nothing gets.
    /// </summary>
    /// <remarks>
    /// CamelCase, to match the property names an enum value sits beside - a camelCase document
    /// carrying a PascalCase value reads as an oversight, because it was one.
    /// </remarks>
    public const string DefaultNaming = "CamelCase";

    /// <summary>
    /// The naming an enum resolves to: its own attribute, else the assembly's, else the default.
    /// </summary>
    public static string For(INamedTypeSymbol enumType, string? assemblyNaming) =>
        Declared(enumType) ?? assemblyNaming ?? DefaultNaming;

    /// <summary>
    /// The default declared by the assembly an enum is defined in, if it declared one.
    /// </summary>
    /// <remarks>
    /// The enum's own assembly rather than the one being compiled: a model type referenced from a
    /// shared library carries its own vocabulary, and re-naming someone else's enum from the
    /// consuming application would change a contract that library already publishes.
    /// </remarks>
    public static string? AssemblyDefault(INamedTypeSymbol enumType) =>
        enumType.ContainingAssembly == null ? null : Declared(enumType.ContainingAssembly);

    /// <summary>
    /// The naming declared directly on a symbol, or null when it carries no attribute.
    /// </summary>
    public static string? Declared(ISymbol symbol) {
        foreach (var attribute in symbol.GetAttributes()) {
            var attributeClass = attribute.AttributeClass;

            if (attributeClass?.Name != AttributeName ||
                attributeClass.ContainingNamespace?.ToDisplayString() != AttributeNamespace) {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 0) {
                continue;
            }

            var value = attribute.ConstructorArguments[0].Value;

            // The argument is the enum's underlying int. Its member name is the naming's name,
            // which is what every consumer here switches on.
            if (value is int ordinal) {
                return NameOf(ordinal);
            }
        }

        return null;
    }

    private static string? NameOf(int ordinal) => ordinal switch {
        0 => "MemberName",
        1 => "CamelCase",
        2 => "KebabCaseLower",
        3 => "SnakeCaseLower",
        4 => "SnakeCaseUpper",
        _ => null
    };

    /// <summary>
    /// The wire value for one member, under a resolved naming.
    /// </summary>
    public static string Value(string memberName, string naming) => naming switch {
        "CamelCase" => CamelCase(memberName),
        "KebabCaseLower" => Delimited(memberName, '-', upper: false),
        "SnakeCaseLower" => Delimited(memberName, '_', upper: false),
        "SnakeCaseUpper" => Delimited(memberName, '_', upper: true),
        _ => memberName
    };

    /// <summary>
    /// Whether Hardened gives this enum a vocabulary at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only enums the application owns. A model graph reaches further than it looks - a property
    /// typed <c>Exception</c> or <c>Type</c> pulls in <c>System.Reflection.MethodAttributes</c> -
    /// and renaming a BCL enum's values is redefining a vocabulary that is not the application's to
    /// redefine. Ownership is the enum's own assembly matching the model's, or that assembly having
    /// declared a default, which is how a shared model library opts in.
    /// </para>
    /// <para>
    /// <c>[Flags]</c> is excluded whoever owns it. A flags value is a combination of members rather
    /// than one of them, so there is no member name to write and no single value to read back;
    /// <c>Static | Final</c> is a number and belongs on the wire as one.
    /// </para>
    /// </remarks>
    public static bool IsOwned(INamedTypeSymbol enumType, IAssemblySymbol? modelAssembly) {
        if (HasFlags(enumType)) {
            return false;
        }

        var assembly = enumType.ContainingAssembly;

        if (assembly == null) {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(assembly, modelAssembly) ||
               Declared(assembly) != null;
    }

    private static bool HasFlags(INamedTypeSymbol enumType) =>
        enumType.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.Name == "FlagsAttribute" &&
            attribute.AttributeClass.ContainingNamespace?.ToDisplayString() == "System");

    /// <summary>
    /// Every distinct member of an enum, paired with the value it goes out as.
    /// </summary>
    /// <remarks>
    /// Distinct by underlying value and by wire value, because neither is guaranteed unique and a
    /// duplicate is not a warning. Two members sharing a value - <c>PrivateScope</c> and
    /// <c>ReuseSlot</c> are both 0 - give two switch arms on one constant, which is CS8510 and does
    /// not compile; two members naming the same wire value give the same on the read side. The first
    /// declared wins, which is the same one <c>Enum.ToString</c> picks.
    /// </remarks>
    public static IReadOnlyList<(string Member, string Wire)> Members(
        INamedTypeSymbol enumType, string naming) {
        var members = new List<(string Member, string Wire)>();
        var seenValues = new HashSet<object>();
        var seenWire = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>()) {
            if (!field.IsConst || field.ConstantValue == null) {
                continue;
            }

            if (!seenValues.Add(field.ConstantValue)) {
                continue;
            }

            var wire = Value(field.Name, naming);

            if (!seenWire.Add(wire)) {
                continue;
            }

            members.Add((field.Name, wire));
        }

        return members;
    }

    private static string CamelCase(string name) {
        if (name.Length == 0 || !char.IsUpper(name[0])) {
            return name;
        }

        // An acronym run keeps going until the last capital before a lower-case letter, so IOStream
        // becomes ioStream rather than iOStream. A name that is all capitals lowercases entirely.
        var prefix = 1;

        while (prefix < name.Length && char.IsUpper(name[prefix])) {
            prefix++;
        }

        // Only when a run of capitals actually ran on into a word. Without the first test a
        // single leading capital steps back to zero and nothing is lowered at all, so CamelCase
        // returned its input for every ordinary name - Standard, Low, Open - which is nearly all
        // of them.
        if (prefix > 1 && prefix < name.Length) {
            prefix--;
        }

        return name.Substring(0, prefix).ToLowerInvariant() + name.Substring(prefix);
    }

    /// <summary>
    /// Word boundaries become <paramref name="separator"/>.
    /// </summary>
    /// <remarks>
    /// A boundary is a capital following a lower-case letter or a digit, or the last capital of a
    /// run that is followed by a lower-case letter - so <c>InProgress</c> is two words,
    /// <c>HTTPProxy</c> is <c>http-proxy</c> rather than <c>h-t-t-p-proxy</c>, and <c>Plus1</c>
    /// stays one word.
    /// </remarks>
    private static string Delimited(string name, char separator, bool upper) {
        var builder = new StringBuilder(name.Length + 4);

        for (var index = 0; index < name.Length; index++) {
            var current = name[index];

            if (index > 0 && char.IsUpper(current)) {
                var previous = name[index - 1];
                var startsWord = !char.IsUpper(previous);
                var endsAcronym = char.IsUpper(previous) &&
                                  index + 1 < name.Length &&
                                  char.IsLower(name[index + 1]);

                if (startsWord || endsAcronym) {
                    builder.Append(separator);
                }
            }

            builder.Append(upper ? char.ToUpperInvariant(current) : char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
