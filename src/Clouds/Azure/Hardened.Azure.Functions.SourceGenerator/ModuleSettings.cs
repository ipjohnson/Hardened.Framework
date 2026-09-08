using System.Text;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Azure.Functions.SourceGenerator;

/// <summary>
/// One setting written on a module attribute: the expression as written, and its value where the
/// expression is a literal.
/// </summary>
/// <remarks>
/// The text is what the binding attribute re-emits, so any expression the application could write
/// on its module it can write here. The value is what the metadata carries, and the host reads the
/// metadata rather than the compiled attribute - so a setting that is not a literal has a text the
/// shim can use and no value the provider can, which is HRDAZ004.
/// </remarks>
internal sealed class Setting {
    public Setting(string text, string? literal, bool? flag, string? number) {
        Text = text;
        Literal = literal;
        Flag = flag;
        Number = number;
    }

    /// <summary>The C# expression as the application wrote it, qualified.</summary>
    public string Text { get; }

    /// <summary>The value, when the expression is a string literal.</summary>
    public string? Literal { get; }

    /// <summary>The value, when the expression is <c>true</c> or <c>false</c>.</summary>
    public bool? Flag { get; }

    /// <summary>The digits, when the expression is an integer literal.</summary>
    public string? Number { get; }

    /// <summary>Whether the generator can carry the value into the metadata.</summary>
    public bool IsLiteral => Literal != null || Flag != null || Number != null;
}

/// <summary>
/// What an application wrote on the module attribute a family's runtime package declares.
/// </summary>
/// <remarks>
/// <para>
/// <c>[ServiceBusModule(Subscription = "orders-service")]</c> on the entry point is how a
/// deployment fact reaches both halves of the line: the module reads it at start to configure
/// the adapter, and this reads it at build to write the function's binding. The entry point model
/// already carries every attribute on the class as its qualified text, which is enough - a module
/// property is a literal by the nature of what it names.
/// </para>
/// <para>
/// Matched on the attribute's own name, which DependencyModules generates as the module's name
/// with <c>Attribute</c> appended: the module arrives as a string from MSBuild and the attribute
/// as a resolved type, the same comparison <c>TriggerModuleGenerator</c> makes.
/// </para>
/// </remarks>
internal sealed class ModuleSettings {
    private readonly IReadOnlyDictionary<string, Setting> _settings;

    private ModuleSettings(IReadOnlyDictionary<string, Setting> settings, bool written) {
        _settings = settings;
        Written = written;
    }

    public static readonly ModuleSettings None = new(new Dictionary<string, Setting>(StringComparer.Ordinal), written: false);

    /// <summary>
    /// Whether the application wrote the module attribute at all, settings or no settings. An
    /// application that names <c>[HttpModule]</c> beside <c>[HardenedModule]</c> is saying its
    /// web routes live in another project, the way a Lambda host names its API Gateway module.
    /// </summary>
    public bool Written { get; }

    public Setting? Get(string name) => _settings.TryGetValue(name, out var setting) ? setting : null;

    /// <summary>The settings on <paramref name="module"/>'s attribute, or none when it is not applied.</summary>
    public static ModuleSettings For(EntryPointSelector.Model entryPoint, string module) {
        var attribute = module.Substring(module.LastIndexOf('.') + 1) + "Attribute";

        foreach (var model in entryPoint.AttributeModels) {
            if (model.TypeDefinition.Name == attribute) {
                return new ModuleSettings(Parse(model.PropertyAssignment), written: true);
            }
        }

        return None;
    }

    /// <summary>
    /// <c>Name = value, Name = value</c> as the attribute model carries it, split outside string
    /// literals so a value holding a comma survives.
    /// </summary>
    internal static IReadOnlyDictionary<string, Setting> Parse(string propertyAssignment) {
        var settings = new Dictionary<string, Setting>(StringComparer.Ordinal);

        foreach (var assignment in Split(propertyAssignment)) {
            var equals = assignment.IndexOf('=');

            if (equals < 1) {
                continue;
            }

            var name = assignment.Substring(0, equals).Trim();
            var text = assignment.Substring(equals + 1).Trim();

            if (name.Length == 0 || text.Length == 0) {
                continue;
            }

            settings[name] = new Setting(text, Literal(text), Flag(text), Number(text));
        }

        return settings;
    }

    private static IEnumerable<string> Split(string text) {
        var start = 0;
        var inString = false;
        var verbatim = false;
        var depth = 0;

        for (var index = 0; index < text.Length; index++) {
            var character = text[index];

            if (inString) {
                if (verbatim) {
                    if (character == '"') {
                        if (index + 1 < text.Length && text[index + 1] == '"') {
                            index++;
                        }
                        else {
                            inString = false;
                        }
                    }
                }
                else if (character == '\\') {
                    index++;
                }
                else if (character == '"') {
                    inString = false;
                }

                continue;
            }

            switch (character) {
                case '"':
                    inString = true;
                    verbatim = index > 0 && text[index - 1] == '@';
                    break;
                case '(':
                case '[':
                case '{':
                    depth++;
                    break;
                case ')':
                case ']':
                case '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return text.Substring(start, index - start);
                    start = index + 1;
                    break;
            }
        }

        if (start < text.Length) {
            yield return text.Substring(start);
        }
    }

    /// <summary>The string a literal denotes, or null for any other expression.</summary>
    internal static string? Literal(string text) {
        if (text.Length >= 3 && text.StartsWith("@\"", StringComparison.Ordinal) && text.EndsWith("\"", StringComparison.Ordinal)) {
            return text.Substring(2, text.Length - 3).Replace("\"\"", "\"");
        }

        if (text.Length < 2 || text[0] != '"' || text[text.Length - 1] != '"') {
            return null;
        }

        var builder = new StringBuilder(text.Length);

        for (var index = 1; index < text.Length - 1; index++) {
            var character = text[index];

            if (character != '\\' || index + 1 >= text.Length - 1) {
                builder.Append(character);
                continue;
            }

            index++;

            switch (text[index]) {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                default:
                    // \" \\ \' and anything else this generator has no reason to interpret.
                    builder.Append(text[index]);
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool? Flag(string text) =>
        text == "true" ? true : text == "false" ? false : null;

    /// <summary>The digits of a non-negative integer literal, or null for any other expression.</summary>
    private static string? Number(string text) {
        if (text.Length == 0) {
            return null;
        }

        foreach (var character in text) {
            if (character < '0' || character > '9') {
                return null;
            }
        }

        return text;
    }
}
