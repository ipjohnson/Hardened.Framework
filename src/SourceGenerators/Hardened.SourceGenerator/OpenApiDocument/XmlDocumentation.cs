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
/// </remarks>
internal static class XmlDocumentation {

    public static (string? Summary, string? Description) Read(SyntaxNode node) {
        var comment = node.GetLeadingTrivia()
            .Select(trivia => trivia.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

        if (comment == null) {
            return (null, null);
        }

        return (Element(comment, "summary"), Element(comment, "remarks"));
    }

    private static string? Element(DocumentationCommentTriviaSyntax comment, string name) {
        foreach (var element in comment.ChildNodes().OfType<XmlElementSyntax>()) {
            if (element.StartTag.Name.LocalName.ValueText != name) {
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
    /// A <c>&lt;see cref="Thing"/&gt;</c> contributes the name it points at, since dropping it
    /// silently would turn "see <c>Thing</c> for the ordering" into "see for the ordering".
    /// </remarks>
    private static string Flatten(XmlElementSyntax element) {
        var builder = new StringBuilder();

        foreach (var token in element.Content.SelectMany(node => node.DescendantTokens())) {
            switch (token.Kind()) {
                case SyntaxKind.XmlTextLiteralToken:
                    builder.Append(token.ValueText);
                    break;

                case SyntaxKind.XmlTextLiteralNewLineToken:
                    builder.Append(' ');
                    break;
            }
        }

        foreach (var reference in element.Content
                     .SelectMany(node => node.DescendantNodes())
                     .OfType<XmlCrefAttributeSyntax>()) {
            builder.Append(' ').Append(SimpleName(reference.Cref.ToString()));
        }

        return Collapse(builder.ToString());
    }

    /// <summary>The last segment of a cref, which is how the type reads in prose.</summary>
    private static string SimpleName(string cref) {
        var lastDot = cref.LastIndexOf('.');

        return lastDot >= 0 && lastDot < cref.Length - 1 ? cref.Substring(lastDot + 1) : cref;
    }

    private static string Collapse(string text) {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text) {
            if (char.IsWhiteSpace(character)) {
                pendingSpace = builder.Length > 0;

                continue;
            }

            if (pendingSpace) {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
