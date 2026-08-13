namespace Hardened.OpenApi.SourceGenerator;

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

    public static string ToPascalCase(string input) {
        if (string.IsNullOrEmpty(input)) return input;

        var parts = input.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var result = "";

        foreach (var part in parts) {
            if (part.Length == 0) continue;
            result += char.ToUpperInvariant(part[0]) + part.Substring(1);
        }

        return string.IsNullOrEmpty(result) ? input : result;
    }

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
