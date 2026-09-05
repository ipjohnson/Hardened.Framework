using System.Linq;
using Hardened.SourceGenerator.OpenApiDocument;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Hardened.SourceGenerator.Tests.OpenApiDocument;

/// <summary>
/// A handler's doc comment, read whether or not the host asked for structured trivia.
/// </summary>
/// <remarks>
/// <para>
/// The reader's own remarks used to say it read syntax rather than
/// <c>GetDocumentationCommentXml</c> so the document would not depend on whether a project emits an
/// XML file - and reading structured trivia has exactly that dependency. Roslyn only builds a
/// <c>DocumentationCommentTriviaSyntax</c> when the parse options ask it to; under
/// <c>DocumentationMode.None</c> a <c>///</c> line is ordinary comment trivia and there is nothing
/// structured to find.
/// </para>
/// <para>
/// So each case runs twice, under both parse modes. <c>None</c> is the one that was silently
/// producing nothing, and it is the mode a project gets by default.
/// </para>
/// </remarks>
public class XmlDocumentationTests {

    private const string Source = """
        class C {
            /// <summary>Echoes a path token back.</summary>
            /// <remarks>Longer prose, <see cref="System.String"/> included.</remarks>
            /// <param name="id">The token to echo.</param>
            public string FromPath(string id) => id;
        }
        """;

    private static MethodDeclarationSyntax Method(DocumentationMode mode) =>
        CSharpSyntaxTree
            .ParseText(Source, new CSharpParseOptions(documentationMode: mode),
                cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();

    public static TheoryData<DocumentationMode> Modes => new() {
        DocumentationMode.Parse,
        DocumentationMode.None,
    };

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheSummaryIsRead(DocumentationMode mode) =>
        Assert.Equal("Echoes a path token back.", XmlDocumentation.Read(Method(mode)).Summary);

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheRemarksBecomeTheDescription(DocumentationMode mode) =>
        Assert.StartsWith("Longer prose,", XmlDocumentation.Read(Method(mode)).Description);

    [Theory]
    [MemberData(nameof(Modes))]
    public void AParamTagIsReadByName(DocumentationMode mode) =>
        Assert.Equal("The token to echo.", XmlDocumentation.ReadParameter(Method(mode), "id"));

    [Theory]
    [MemberData(nameof(Modes))]
    public void AParameterTheCommentDoesNotMentionReadsAsNothing(DocumentationMode mode) =>
        Assert.Null(XmlDocumentation.ReadParameter(Method(mode), "somethingElse"));

    [Theory]
    [MemberData(nameof(Modes))]
    public void AMemberWithNoCommentReadsAsNothing(DocumentationMode mode) {
        var method = CSharpSyntaxTree
            .ParseText("class C { public string M() => \"\"; }",
                new CSharpParseOptions(documentationMode: mode),
                cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

        var (summary, description) = XmlDocumentation.Read(method);

        Assert.Null(summary);
        Assert.Null(description);
        Assert.Null(XmlDocumentation.ReadParameter(method, "id"));
    }

    private const string Entities = """
        class C {
            /// <summary>Created&lt;T&gt; carries the body &amp; the Location, &quot;both&quot;.</summary>
            /// <remarks>Copyright &#169; and &#x263A; are characters too; a stray & is prose.</remarks>
            public string Create() => "";
        }
        """;

    private static MethodDeclarationSyntax Entity(DocumentationMode mode) =>
        CSharpSyntaxTree
            .ParseText(Entities, new CSharpParseOptions(documentationMode: mode),
                cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();

    /// <summary>
    /// A doc comment has to write <c>&amp;lt;</c> to say <c>&lt;</c>, and the template's own
    /// exported document carried <c>Created&amp;lt;T&amp;gt;</c> through as text. Both paths decode.
    /// </summary>
    [Theory]
    [MemberData(nameof(Modes))]
    public void AnEntityIsTheCharacterItStandsFor(DocumentationMode mode) =>
        Assert.Equal(
            "Created<T> carries the body & the Location, \"both\".",
            XmlDocumentation.Read(Entity(mode)).Summary);

    [Theory]
    [MemberData(nameof(Modes))]
    public void ANumericEntityIsDecodedAndAStrayAmpersandIsKept(DocumentationMode mode) =>
        Assert.Equal(
            "Copyright \u00a9 and \u263a are characters too; a stray & is prose.",
            XmlDocumentation.Read(Entity(mode)).Description);
}
