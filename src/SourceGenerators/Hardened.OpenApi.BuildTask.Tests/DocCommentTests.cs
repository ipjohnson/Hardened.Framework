using Hardened.Idl.Emitters;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// What specification prose has to survive to be usable after a <c>///</c>.
/// </summary>
public class DocCommentTests {

    [Fact]
    public void OrdinaryProseIsUnchanged() {
        Assert.Equal("Returns a single pet.", DocComment.Format("Returns a single pet."));
    }

    /// <summary>
    /// Paragraphs are kept. This used to collapse to one line, because the component it feeds
    /// wrote a comment as a single indented line and a surviving newline emitted one carrying
    /// neither the indent nor the marker. CSharpAuthor writes them line by line now.
    /// </summary>
    [Fact]
    public void LineBreaksAreKept() {
        Assert.Equal(
            "One.\nTwo.\n\nThree.",
            DocComment.Format("One.\nTwo.\r\n\r\nThree."));
    }

    /// <summary>
    /// Carriage returns are normalised away, so a spec authored on Windows does not leave one
    /// sitting inside a generated comment.
    /// </summary>
    [Fact]
    public void CarriageReturnsAreNormalised() {
        Assert.DoesNotContain("\r", DocComment.Format("One.\r\nTwo.")!);
    }

    /// <summary>
    /// Trailing whitespace goes, since it would show up as a whitespace-only diff on every
    /// regeneration. Leading indentation is content and stays.
    /// </summary>
    [Fact]
    public void TrailingWhitespaceIsTrimmedPerLine() {
        Assert.Equal("One.\nTwo.", DocComment.Format("One.   \nTwo.\t"));
    }

    /// <summary>
    /// Blank lines around the whole description carry nothing and would render as a bare marker
    /// against the summary tags. Blank lines between paragraphs are structure and stay.
    /// </summary>
    [Fact]
    public void SurroundingBlankLinesAreDroppedButInnerOnesAreNot() {
        Assert.Equal("One.\n\nTwo.", DocComment.Format("\n\nOne.\n\nTwo.\n\n"));
    }

    /// <summary>
    /// A description is XML content once it is inside a doc comment, so the three characters that
    /// are markup have to stop being markup.
    /// </summary>
    [Fact]
    public void MarkupCharactersAreEscaped() {
        Assert.Equal(
            "0 &lt; n &lt;= 100 &amp; n &gt; 0",
            DocComment.Format("0 < n <= 100 & n > 0"));
    }

    [Fact]
    public void AmpersandsInEscapesAreThemselvesEscaped() {
        Assert.Equal("&amp;lt; is a less-than", DocComment.Format("&lt; is a less-than"));
    }

    /// <summary>
    /// Nothing to say produces no comment rather than an empty one, which is what lets the caller
    /// leave the existing route-only comment alone.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void NothingToSayProducesNull(string? description) {
        Assert.Null(DocComment.Format(description));
    }
}
