using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// A handler's doc comment, as the two fields an OpenAPI operation has for prose.
/// </summary>
/// <remarks>
/// <para>
/// <c>&lt;summary&gt;</c> becomes <c>summary</c> and <c>&lt;remarks&gt;</c> becomes
/// <c>description</c>, which is the same split the two constructs already have: one line saying
/// what the operation is, and as much as is needed saying how it behaves. The specification-first
/// direction reads them back in that order - <c>FirstNonEmpty(operation.Summary,
/// operation.Description)</c> - so a doc comment survives a round trip as a doc comment.
/// </para>
/// <para>
/// Read from syntax rather than through <c>ISymbol.GetDocumentationCommentXml</c>, which returns
/// nothing unless the compilation was parsed with <c>DocumentationMode.Parse</c>. A generator does
/// not choose its host's parse options, so relying on that would make the document depend on
/// whether the project happens to emit an XML documentation file.
/// </para>
/// <para>
/// An entity is the character it stands for. A doc comment has to write <c>Created&amp;lt;T&amp;gt;</c>
/// to say <c>Created&lt;T&gt;</c>, and the template's own exported document carried the entities
/// through as text; both paths decode them, the structured one through the entity token Roslyn
/// already produces and the raw one by hand.
/// </para>
/// </remarks>
internal static class XmlDocumentation
{
    public static (string? Summary, string? Description) Read(SyntaxNode node)
    {
        var comment = node.GetLeadingTrivia()
            .Select(trivia => trivia.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

        if (comment == null)
        {
            return FromRawTrivia(node);
        }

        return (Element(comment, "summary"), Element(comment, "remarks"));
    }

    /// <summary>
    /// The prose a doc comment gives one named parameter, or null.
    /// </summary>
    /// <remarks>
    /// <c>&lt;param name="petId"&gt;</c> is where a developer has already written what a parameter
    /// means, and an OpenAPI parameter has a <c>description</c> for exactly that. Read through the
    /// same two paths as the summary, for the same reason.
    /// </remarks>
    public static string? ReadParameter(SyntaxNode node, string parameterName)
    {
        var comment = node.GetLeadingTrivia()
            .Select(trivia => trivia.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

        if (comment == null)
        {
            return RawParameter(node, parameterName);
        }

        foreach (var element in comment.ChildNodes().OfType<XmlElementSyntax>())
        {
            if (element.StartTag.Name.LocalName.ValueText != "param")
            {
                continue;
            }

            foreach (var attribute in element.StartTag.Attributes.OfType<XmlNameAttributeSyntax>())
            {
                if (attribute.Identifier.Identifier.ValueText != parameterName)
                {
                    continue;
                }

                var text = Flatten(element);

                return text.Length > 0 ? text : null;
            }
        }

        return null;
    }

    private static string? RawParameter(SyntaxNode node, string parameterName)
    {
        var text = RawText(node);

        if (text == null)
        {
            return null;
        }

        var open = text.IndexOf(
            "<param name=\"" + parameterName + "\">",
            System.StringComparison.Ordinal
        );

        if (open < 0)
        {
            return null;
        }

        var start = text.IndexOf('>', open) + 1;
        var close = text.IndexOf("</param>", start, System.StringComparison.Ordinal);

        if (close < 0)
        {
            return null;
        }

        var inner = StripTags(text.Substring(start, close - start));

        return inner.Length > 0 ? inner : null;
    }

    /// <summary>
    /// The same two elements, read out of unstructured <c>///</c> trivia.
    /// </summary>
    /// <remarks>
    /// Reached when the compilation was parsed with <c>DocumentationMode.None</c>, where the lines
    /// are present as comments and carry no structure. Matched rather than parsed as XML: the
    /// content is prose that may not be well-formed, and a doc comment that fails to parse should
    /// contribute nothing rather than fail a build that was otherwise fine.
    /// </remarks>
    private static (string? Summary, string? Description) FromRawTrivia(SyntaxNode node)
    {
        var text = RawText(node);

        return text == null
            ? (null, null)
            : (RawElement(text, "summary"), RawElement(text, "remarks"));
    }

    /// <summary>The <c>///</c> lines above a node, joined, with the slashes removed.</summary>
    private static string? RawText(SyntaxNode node)
    {
        var builder = new StringBuilder();

        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                continue;
            }

            var line = trivia.ToString().TrimStart();

            if (!line.StartsWith("///", System.StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(line.Substring(3)).Append(' ');
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string? RawElement(string text, string name)
    {
        var open = text.IndexOf("<" + name + ">", System.StringComparison.Ordinal);

        if (open < 0)
        {
            return null;
        }

        var start = open + name.Length + 2;
        var close = text.IndexOf("</" + name + ">", start, System.StringComparison.Ordinal);

        if (close < 0)
        {
            return null;
        }

        var inner = StripTags(text.Substring(start, close - start));

        return inner.Length > 0 ? inner : null;
    }

    /// <summary>
    /// The prose inside an element, with any nested markup removed and each self-closing
    /// reference read the way <see cref="AppendReference"/> reads it.
    /// </summary>
    private static string StripTags(string text)
    {
        var builder = new StringBuilder(text.Length);
        var depth = 0;
        var tagStart = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (character == '<')
            {
                if (depth == 0)
                {
                    tagStart = index + 1;
                }

                depth++;
            }
            else if (character == '>')
            {
                if (depth > 0)
                {
                    depth--;

                    if (depth == 0)
                    {
                        builder.Append(RawReference(text.Substring(tagStart, index - tagStart)));
                    }
                }
            }
            else if (depth == 0)
            {
                builder.Append(character);
            }
        }

        return Collapse(DecodeEntities(builder.ToString()));
    }

    /// <summary>
    /// What a self-closing tag's text reads as in prose, or nothing for any other tag.
    /// </summary>
    /// <param name="tag">The text between the tag's angle brackets.</param>
    private static string RawReference(string tag)
    {
        if (!tag.EndsWith("/", System.StringComparison.Ordinal))
        {
            return "";
        }

        foreach (var attribute in new[] { "cref", "name", "langword" })
        {
            var marker = " " + attribute + "=\"";
            var start = tag.IndexOf(marker, System.StringComparison.Ordinal);

            if (start < 0)
            {
                continue;
            }

            start += marker.Length;

            var end = tag.IndexOf('"', start);

            if (end < 0)
            {
                return "";
            }

            var value = tag.Substring(start, end - start);

            return attribute == "cref" ? SimpleName(value) : value;
        }

        return "";
    }

    /// <summary>
    /// The five named entities XML defines and the numeric forms, as their characters. Anything
    /// else is left as written: a stray <c>&amp;</c> in prose is prose.
    /// </summary>
    private static string DecodeEntities(string text)
    {
        if (text.IndexOf('&') < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            var ampersand = text.IndexOf('&', index);

            if (ampersand < 0)
            {
                builder.Append(text, index, text.Length - index);

                break;
            }

            builder.Append(text, index, ampersand - index);

            var semicolon = text.IndexOf(';', ampersand);

            if (
                semicolon > ampersand
                && Decode(text.Substring(ampersand + 1, semicolon - ampersand - 1)) is { } decoded
            )
            {
                builder.Append(decoded);
                index = semicolon + 1;
            }
            else
            {
                builder.Append('&');
                index = ampersand + 1;
            }
        }

        return builder.ToString();
    }

    private static string? Decode(string entity)
    {
        switch (entity)
        {
            case "lt":
                return "<";
            case "gt":
                return ">";
            case "amp":
                return "&";
            case "quot":
                return "\"";
            case "apos":
                return "'";
        }

        if (entity.Length < 2 || entity[0] != '#')
        {
            return null;
        }

        var hex = entity[1] == 'x' || entity[1] == 'X';
        var digits = entity.Substring(hex ? 2 : 1);

        return
            int.TryParse(
                digits,
                hex
                    ? System.Globalization.NumberStyles.HexNumber
                    : System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var codePoint
            )
            && codePoint > 0
            && codePoint <= 0x10FFFF
            ? char.ConvertFromUtf32(codePoint)
            : null;
    }

    private static string? Element(DocumentationCommentTriviaSyntax comment, string name)
    {
        foreach (var element in comment.ChildNodes().OfType<XmlElementSyntax>())
        {
            if (element.StartTag.Name.LocalName.ValueText != name)
            {
                continue;
            }

            var text = Flatten(element);

            return text.Length > 0 ? text : null;
        }

        return null;
    }

    /// <summary>
    /// The prose, with the markup removed and the line breaks a doc comment carries collapsed to
    /// single spaces - a JSON string field is not the place for <c>///</c> and hanging indentation.
    /// </summary>
    /// <remarks>
    /// A <c>&lt;see cref="Thing"/&gt;</c> contributes the name it points at, where it stands, since
    /// dropping it silently would turn "see <c>Thing</c> for the ordering" into "see for the
    /// ordering". The names used to be appended after the prose, where "Thing" read as a stray
    /// word at the end of the description.
    /// </remarks>
    private static string Flatten(XmlElementSyntax element)
    {
        var builder = new StringBuilder();

        AppendContent(builder, element.Content);

        return Collapse(builder.ToString());
    }

    /// <summary>The prose of <paramref name="content"/>, in document order.</summary>
    private static void AppendContent(StringBuilder builder, SyntaxList<XmlNodeSyntax> content)
    {
        foreach (var node in content)
        {
            switch (node)
            {
                case XmlElementSyntax nested:
                    AppendContent(builder, nested.Content);
                    break;

                case XmlEmptyElementSyntax empty:
                    AppendReference(builder, empty);
                    break;

                default:
                    foreach (var token in node.DescendantTokens())
                    {
                        AppendToken(builder, token);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// What a self-closing tag reads as in prose: the name a <c>cref</c> points at, the name a
    /// <c>paramref</c> or <c>typeparamref</c> gives, or a <c>langword</c> as written.
    /// </summary>
    private static void AppendReference(StringBuilder builder, XmlEmptyElementSyntax element)
    {
        foreach (var attribute in element.Attributes)
        {
            switch (attribute)
            {
                case XmlCrefAttributeSyntax cref:
                    builder.Append(SimpleName(cref.Cref.ToString()));
                    return;

                case XmlNameAttributeSyntax name:
                    builder.Append(name.Identifier.Identifier.ValueText);
                    return;

                case XmlTextAttributeSyntax text when text.Name.LocalName.ValueText == "langword":
                    foreach (var token in text.TextTokens)
                    {
                        AppendToken(builder, token);
                    }

                    return;
            }
        }
    }

    private static void AppendToken(StringBuilder builder, SyntaxToken token)
    {
        switch (token.Kind())
        {
            case SyntaxKind.XmlTextLiteralToken:
            case SyntaxKind.XmlEntityLiteralToken:
                builder.Append(token.ValueText);
                break;

            case SyntaxKind.XmlTextLiteralNewLineToken:
                builder.Append(' ');
                break;
        }
    }

    /// <summary>
    /// The last segment of a cref, which is how the type reads in prose: without the parameter
    /// list a method cref carries, and with a generic's braces written as C# writes them.
    /// </summary>
    private static string SimpleName(string cref)
    {
        var parameters = cref.IndexOf('(');
        var name = parameters >= 0 ? cref.Substring(0, parameters) : cref;
        var typeArguments = name.IndexOf('{');
        var lastDot = (typeArguments >= 0 ? name.Substring(0, typeArguments) : name).LastIndexOf(
            '.'
        );

        if (lastDot >= 0 && lastDot < name.Length - 1)
        {
            name = name.Substring(lastDot + 1);
        }

        return name.Replace('{', '<').Replace('}', '>');
    }

    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;

                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
