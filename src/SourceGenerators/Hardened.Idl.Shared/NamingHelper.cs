using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hardened.Idl;

internal static class NamingHelper {
    private static readonly HashSet<string> CSharpKeywords = new() {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while"
    };

    /// <summary>
    /// A spec name as a C# identifier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Splitting on <c>_ - space</c> and passing everything else through was the single defect
    /// behind most of the invalid code this generator produced: Twilio's <c>api.v2010.account</c>
    /// became <c>record Api.v2010.account</c>, Slack's <c>admin.apps</c> tag became
    /// <c>interface IAdmin.appsService</c>, and GitHub's <c>Won't fix</c> became an unterminated
    /// character literal. A spec file named <c>min-3.0.3.yaml</c> was enough on its own.
    /// </para>
    /// <para>
    /// Symbols are <b>replaced, not deleted</b>. Dropping them collapses names that mean different
    /// things: GitHub declares both <c>+1</c> and <c>-1</c> on one object, and Twilio has
    /// <c>StartTime&lt;</c> beside <c>StartTime&gt;</c>. Deletion makes those collide; a word for
    /// each keeps them apart and keeps them readable.
    /// </para>
    /// <para>
    /// Total over every input. Anything with no better mapping becomes <c>U</c> plus its code
    /// point, because a character nobody anticipated must still produce an identifier - real
    /// documents carry non-ASCII property names and emoji in enum values.
    /// </para>
    /// </remarks>
    public static string ToPascalCase(string input) {
        // A name can be absent and still be a name. Docker and Cloudflare both declare an enum
        // whose value set includes the empty string, and PagerDuty declares a property called it -
        // all of which reached C# as nothing at all, leaving `enum X { , Inactive }` and a record
        // parameter with a type and no identifier. The wire name stays empty either way; only the
        // member needs something to be called.
        if (string.IsNullOrWhiteSpace(input)) return "Empty";

        var tokens = new List<string>();
        var current = new StringBuilder();

        void Flush() {
            if (current.Length > 0) {
                tokens.Add(current.ToString());
                current.Length = 0;
            }
        }

        for (var i = 0; i < input.Length; i++) {
            var character = input[i];

            if (char.IsLetterOrDigit(character)) {
                current.Append(character);
                continue;
            }

            // A sign rather than a separator: '-' only leads a token when a digit follows and
            // nothing precedes it, which is what separates GitHub's "-1" from its
            // "marketplace-purchase".
            var leadsToken = current.Length == 0 && tokens.Count == 0;
            var digitFollows = i + 1 < input.Length && char.IsDigit(input[i + 1]);

            if (character == '-' && leadsToken && digitFollows) {
                tokens.Add("Minus");
                continue;
            }

            var word = SymbolWord(character);

            if (word != null) {
                Flush();
                tokens.Add(word);
                continue;
            }

            if (IsDroppable(character)) {
                // Inside a word: "Won't" is one token, not two.
                continue;
            }

            if (char.IsWhiteSpace(character) || char.IsPunctuation(character) ||
                char.IsSeparator(character) || char.IsControl(character)) {
                Flush();
                continue;
            }

            // A symbol with no word of its own. Kept as its code point rather than dropped, so two
            // names that differ only by it stay two names.
            Flush();
            tokens.Add("U" + ((int)character).ToString("X4", CultureInfo.InvariantCulture));
        }

        Flush();

        var result = new StringBuilder();

        foreach (var token in tokens) {
            result.Append(char.ToUpperInvariant(token[0]));
            if (token.Length > 1) result.Append(token, 1, token.Length - 1);
        }

        if (result.Length == 0) {
            return "Item";
        }

        // An identifier cannot open with a digit, and "+1" already carries its sign as a word.
        if (char.IsDigit(result[0])) {
            result.Insert(0, '_');
        }

        return result.ToString();
    }

    /// <summary>
    /// Symbols that carry meaning, as the word a reader would say aloud.
    /// </summary>
    private static string? SymbolWord(char character) {
        switch (character) {
            case '+': return "Plus";
            case '<': return "LessThan";
            case '>': return "GreaterThan";
            case '=': return "Equals";
            case '&': return "And";
            case '@': return "At";
            case '#': return "Hash";
            case '$': return "Dollar";
            case '%': return "Percent";
            case '*': return "Star";
            case '!': return "Not";
            case '?': return "Maybe";
            case '~': return "Tilde";
            case '^': return "Caret";
            case '|': return "Or";
            default: return null;
        }
    }

    /// <summary>
    /// Punctuation that sits inside a word without dividing it.
    /// </summary>
    private static bool IsDroppable(char character) =>
        character == '\'' || character == '"' || character == '`' || character == '’';

    public static string ToCamelCase(string input) {
        var pascal = ToPascalCase(input);
        if (string.IsNullOrEmpty(pascal)) return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
    }

    public static string EscapeIdentifier(string name) {
        return CSharpKeywords.Contains(name) ? "@" + name : name;
    }

    public static string ToMethodName(string operationId) {
        return ToPascalCase(operationId);
    }

    /// <summary>
    /// A method name for an operation, from the route it answers on.
    /// </summary>
    /// <remarks>
    /// Two callers want the same answer: a description that declares no id for an operation, and
    /// the allocator when an id a description does declare collides with another. It lives here
    /// rather than in either of them because it reads nothing but a verb and a route, which is what
    /// any interface language with HTTP bindings has - not something particular to OpenAPI.
    /// </remarks>
    public static string OperationIdFromRoute(string method, string route) {
        var name = new StringBuilder(method.ToLowerInvariant());

        foreach (var segment in route.Split('/')) {
            if (segment.Length == 0) {
                continue;
            }

            // A parameter reads as what it selects by, so /pets/{petId} is getPetsByPetId rather
            // than getPetsPetId - which says the same thing twice and loses that it was a variable.
            if (segment[0] == '{') {
                name.Append("By");
                name.Append(ToPascalCase(segment.Trim('{', '}')));
            } else {
                name.Append(ToPascalCase(segment));
            }
        }

        return name.ToString();
    }

    public static string ToInterfaceName(string tag) {
        var pascal = ToPascalCase(tag);
        if (pascal.StartsWith("I") && pascal.Length > 1 && char.IsUpper(pascal[1])) {
            return pascal + "Service";
        }
        return "I" + pascal + "Service";
    }

    public static string ToControllerName(string tag) {
        return ToPascalCase(tag) + "Controller";
    }

    public static string ToParameterName(string name) {
        var camel = ToCamelCase(name);
        return EscapeIdentifier(camel);
    }
}
