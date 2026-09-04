using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hardened.Generation.Document;

/// <summary>
/// A JSON value that keeps the order it was written in.
/// </summary>
/// <remarks>
/// <para>
/// The served document is compact JSON assembled by hand in <c>OpenApiDocumentGenerator</c>, and
/// the export writes it out again indented, or as YAML. Neither the generators nor the build task
/// may take a JSON library: an analyzer with a sibling DLL fails at load, so everything the
/// generators use is compiled from source, and this module follows that rule so the task can
/// compile it in the same way. It is a tree with an ordered object, because key order is the one
/// thing a reader of the exported file notices that <c>System.Text.Json</c> would not preserve.
/// </para>
/// <para>
/// Numbers keep their text. The generator writes them the way it wants them read, and re-parsing
/// through <c>double</c> would turn <c>100</c> into <c>100.0</c> or lose precision on a large
/// integer - so a number round-trips as the characters it arrived as.
/// </para>
/// </remarks>
internal abstract class JsonNode {
}

/// <summary>An object, as an ordered list of members.</summary>
internal sealed class JsonObject : JsonNode {
    public List<KeyValuePair<string, JsonNode>> Members { get; } = new List<KeyValuePair<string, JsonNode>>();

    public JsonNode? Get(string key) {
        foreach (var member in Members) {
            if (string.Equals(member.Key, key, StringComparison.Ordinal)) {
                return member.Value;
            }
        }

        return null;
    }

    /// <summary>Replaces the member in place, or appends it when absent.</summary>
    public void Set(string key, JsonNode value) {
        for (var index = 0; index < Members.Count; index++) {
            if (string.Equals(Members[index].Key, key, StringComparison.Ordinal)) {
                Members[index] = new KeyValuePair<string, JsonNode>(key, value);

                return;
            }
        }

        Members.Add(new KeyValuePair<string, JsonNode>(key, value));
    }

    public bool Remove(string key) {
        for (var index = 0; index < Members.Count; index++) {
            if (string.Equals(Members[index].Key, key, StringComparison.Ordinal)) {
                Members.RemoveAt(index);

                return true;
            }
        }

        return false;
    }
}

internal sealed class JsonArray : JsonNode {
    public List<JsonNode> Items { get; } = new List<JsonNode>();
}

internal sealed class JsonString : JsonNode {
    public JsonString(string value) {
        Value = value;
    }

    public string Value { get; }
}

/// <summary>A number, as the text it was written as.</summary>
internal sealed class JsonNumber : JsonNode {
    public JsonNumber(string text) {
        Text = text;
    }

    public string Text { get; }
}

internal sealed class JsonBoolean : JsonNode {
    public static readonly JsonBoolean True = new JsonBoolean(true);

    public static readonly JsonBoolean False = new JsonBoolean(false);

    private JsonBoolean(bool value) {
        Value = value;
    }

    public bool Value { get; }
}

internal sealed class JsonNull : JsonNode {
    public static readonly JsonNull Instance = new JsonNull();

    private JsonNull() {
    }
}

/// <summary>
/// Reads JSON text into a <see cref="JsonNode"/> tree.
/// </summary>
/// <remarks>
/// RFC 8259 in full rather than only what the generator emits, because the file this reads is
/// whatever the compiled assembly carries and a parser that accepted a subset would fail on the
/// first document that used something outside it. Strictness is the same as the standard's: no
/// comments, no trailing commas, no unquoted keys. A defect in the input is a
/// <see cref="FormatException"/> naming the offset.
/// </remarks>
internal static class JsonTree {

    public static JsonNode Parse(string text) {
        var reader = new Reader(text);
        var value = reader.ReadValue();

        reader.SkipWhitespace();

        if (!reader.AtEnd) {
            throw reader.Error("unexpected content after the document");
        }

        return value;
    }

    private struct Reader {
        private readonly string _text;
        private int _position;

        public Reader(string text) {
            _text = text;
            _position = 0;
        }

        public bool AtEnd => _position >= _text.Length;

        public FormatException Error(string what) =>
            new FormatException("Invalid JSON at offset " + _position.ToString(CultureInfo.InvariantCulture) + ": " + what + ".");

        public void SkipWhitespace() {
            while (_position < _text.Length) {
                var ch = _text[_position];

                if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r') {
                    _position++;
                }
                else {
                    break;
                }
            }
        }

        public JsonNode ReadValue() {
            SkipWhitespace();

            if (AtEnd) {
                throw Error("expected a value");
            }

            var ch = _text[_position];

            switch (ch) {
                case '{':
                    return ReadObject();
                case '[':
                    return ReadArray();
                case '"':
                    return new JsonString(ReadString());
                case 't':
                    ReadLiteral("true");
                    return JsonBoolean.True;
                case 'f':
                    ReadLiteral("false");
                    return JsonBoolean.False;
                case 'n':
                    ReadLiteral("null");
                    return JsonNull.Instance;
                default:
                    if (ch == '-' || (ch >= '0' && ch <= '9')) {
                        return ReadNumber();
                    }

                    throw Error("unexpected character '" + ch + "'");
            }
        }

        private JsonObject ReadObject() {
            var result = new JsonObject();

            _position++;
            SkipWhitespace();

            if (Peek() == '}') {
                _position++;

                return result;
            }

            while (true) {
                SkipWhitespace();

                if (Peek() != '"') {
                    throw Error("expected a member name");
                }

                var key = ReadString();

                SkipWhitespace();

                if (Peek() != ':') {
                    throw Error("expected ':' after a member name");
                }

                _position++;

                var value = ReadValue();

                result.Members.Add(new KeyValuePair<string, JsonNode>(key, value));

                SkipWhitespace();

                var next = Peek();

                if (next == ',') {
                    _position++;

                    continue;
                }

                if (next == '}') {
                    _position++;

                    return result;
                }

                throw Error("expected ',' or '}' in an object");
            }
        }

        private JsonArray ReadArray() {
            var result = new JsonArray();

            _position++;
            SkipWhitespace();

            if (Peek() == ']') {
                _position++;

                return result;
            }

            while (true) {
                result.Items.Add(ReadValue());

                SkipWhitespace();

                var next = Peek();

                if (next == ',') {
                    _position++;

                    continue;
                }

                if (next == ']') {
                    _position++;

                    return result;
                }

                throw Error("expected ',' or ']' in an array");
            }
        }

        private char Peek() => _position < _text.Length ? _text[_position] : '\0';

        private void ReadLiteral(string literal) {
            if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0) {
                throw Error("expected '" + literal + "'");
            }

            _position += literal.Length;
        }

        private JsonNumber ReadNumber() {
            var start = _position;

            if (Peek() == '-') {
                _position++;
            }

            if (Peek() == '0') {
                _position++;
            }
            else if (Peek() >= '1' && Peek() <= '9') {
                ReadDigits();
            }
            else {
                throw Error("expected a digit");
            }

            if (Peek() == '.') {
                _position++;

                if (!(Peek() >= '0' && Peek() <= '9')) {
                    throw Error("expected a digit after '.'");
                }

                ReadDigits();
            }

            if (Peek() == 'e' || Peek() == 'E') {
                _position++;

                if (Peek() == '+' || Peek() == '-') {
                    _position++;
                }

                if (!(Peek() >= '0' && Peek() <= '9')) {
                    throw Error("expected a digit in the exponent");
                }

                ReadDigits();
            }

            return new JsonNumber(_text.Substring(start, _position - start));
        }

        private void ReadDigits() {
            while (Peek() >= '0' && Peek() <= '9') {
                _position++;
            }
        }

        private string ReadString() {
            // Past the opening quote.
            _position++;

            StringBuilder? builder = null;
            var runStart = _position;

            while (true) {
                if (AtEnd) {
                    throw Error("unterminated string");
                }

                var ch = _text[_position];

                if (ch == '"') {
                    var run = _text.Substring(runStart, _position - runStart);

                    _position++;

                    if (builder == null) {
                        return run;
                    }

                    builder.Append(run);

                    return builder.ToString();
                }

                if (ch < ' ') {
                    throw Error("a control character must be escaped inside a string");
                }

                if (ch != '\\') {
                    _position++;

                    continue;
                }

                builder ??= new StringBuilder();
                builder.Append(_text, runStart, _position - runStart);

                _position++;

                if (AtEnd) {
                    throw Error("unterminated escape");
                }

                var escaped = _text[_position];

                _position++;

                switch (escaped) {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        builder.Append(ReadHexCharacter());
                        break;
                    default:
                        throw Error("unknown escape '\\" + escaped + "'");
                }

                runStart = _position;
            }
        }

        /// <summary>
        /// The four hex digits after <c>\u</c>. A surrogate pair arrives as two escapes and
        /// appends as two UTF-16 units, which is what a .NET string holds anyway.
        /// </summary>
        private char ReadHexCharacter() {
            if (_position + 4 > _text.Length) {
                throw Error("expected four hex digits after '\\u'");
            }

            var value = 0;

            for (var index = 0; index < 4; index++) {
                var ch = _text[_position + index];
                int digit;

                if (ch >= '0' && ch <= '9') {
                    digit = ch - '0';
                }
                else if (ch >= 'a' && ch <= 'f') {
                    digit = ch - 'a' + 10;
                }
                else if (ch >= 'A' && ch <= 'F') {
                    digit = ch - 'A' + 10;
                }
                else {
                    throw Error("expected four hex digits after '\\u'");
                }

                value = (value << 4) | digit;
            }

            _position += 4;

            return (char)value;
        }
    }
}
