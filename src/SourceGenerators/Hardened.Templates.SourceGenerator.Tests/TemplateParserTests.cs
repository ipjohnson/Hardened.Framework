using Hardened.SourceGenerator.Templates.Parser;

namespace Hardened.Templates.SourceGenerator.Tests;

/// <summary>
/// The parser, driven directly.
///
/// <para>
/// Everything the generator emits is a function of the node tree this produces, so a parse that
/// silently drops a token turns into markup that silently vanishes from the page — the kind of
/// defect no compilation check can see. Going through the parser rather than the whole generator
/// keeps the failure local: a broken assertion here names the token, not a line of emitted C#.
/// </para>
/// </summary>
public class TemplateParserTests {

    private static readonly StringTokenNodeParser.TokenInfo Mustache = new("{{", "}}");

    private static IList<TemplateActionNode> Parse(string template) =>
        new TemplateParseService(new StringTokenNodeParser(new StringTokenNodeCreatorService()))
            .ParseTemplate(template, Mustache);

    // ---------------------------------------------------------------- content and tokens

    [Fact]
    public void TextWithNoTokensIsASingleContentNode() {
        var node = Assert.Single(Parse("<p>hello</p>"));

        Assert.Equal(TemplateActionType.Content, node.Action);
        Assert.Equal("<p>hello</p>", node.ActionText);
    }

    /// <summary>
    /// An empty file still parses to a single empty content node. The emitter drops content whose
    /// text is empty, so nothing reaches the generated class — but the template is still registered,
    /// which is what makes an empty template render as an empty string rather than throwing
    /// "could not locate template".
    /// </summary>
    [Fact]
    public void AnEmptyTemplateProducesOneEmptyContentNode() {
        var node = Assert.Single(Parse(""));

        Assert.Equal(TemplateActionType.Content, node.Action);
        Assert.Equal("", node.ActionText);
    }

    [Fact]
    public void ATokenBetweenTextSplitsIntoThreeNodes() {
        var nodes = Parse("<p>{{Name}}</p>");

        Assert.Equal(3, nodes.Count);
        Assert.Equal(TemplateActionType.Content, nodes[0].Action);
        Assert.Equal(TemplateActionType.MustacheToken, nodes[1].Action);
        Assert.Equal("Name", nodes[1].ActionText);
        Assert.Equal(TemplateActionType.Content, nodes[2].Action);
    }

    [Fact]
    public void ATokenAtTheVeryStartProducesNoLeadingContentNode() {
        var nodes = Parse("{{Name}}tail");

        Assert.Equal(2, nodes.Count);
        Assert.Equal(TemplateActionType.MustacheToken, nodes[0].Action);
    }

    [Fact]
    public void ATokenAtTheVeryEndProducesNoTrailingContentNode() {
        var nodes = Parse("head{{Name}}");

        Assert.Equal(2, nodes.Count);
        Assert.Equal(TemplateActionType.MustacheToken, nodes[1].Action);
    }

    [Fact]
    public void AdjacentTokensBothParse() {
        var nodes = Parse("{{First}}{{Second}}");

        Assert.Equal(2, nodes.Count);
        Assert.Equal("First", nodes[0].ActionText);
        Assert.Equal("Second", nodes[1].ActionText);
    }

    /// <summary>Whitespace inside the braces is not part of the token name.</summary>
    [Theory]
    [InlineData("{{Name}}")]
    [InlineData("{{ Name}}")]
    [InlineData("{{Name }}")]
    [InlineData("{{  Name  }}")]
    public void WhitespaceInsideTheBracesIsTrimmedFromTheToken(string template) {
        var node = Assert.Single(Parse(template));

        Assert.Equal("Name", node.ActionText);
    }

    /// <summary>
    /// Triple braces are a distinct node type, which is what makes the emitter choose
    /// <c>WriteRaw</c> over <c>Write</c> — the difference between escaped and unescaped output.
    /// </summary>
    [Fact]
    public void TripleBracesProduceARawToken() {
        var node = Assert.Single(Parse("{{{Name}}}"));

        Assert.Equal(TemplateActionType.RawMustacheToken, node.Action);
        Assert.Equal("Name", node.ActionText);
    }

    [Fact]
    public void DoubleBracesProduceAnEscapedToken() {
        var node = Assert.Single(Parse("{{Name}}"));

        Assert.Equal(TemplateActionType.MustacheToken, node.Action);
    }

    [Fact]
    public void ADottedPropertyPathSurvivesAsOneToken() {
        var node = Assert.Single(Parse("{{Address.City}}"));

        Assert.Equal("Address.City", node.ActionText);
    }

    // ---------------------------------------------------------------- arguments

    [Fact]
    public void ATokenWithNoArgumentsHasAnEmptyArgumentList() {
        var node = Assert.Single(Parse("{{Name}}"));

        Assert.Empty(node.ArgumentList);
    }

    [Fact]
    public void APropertyArgumentIsParsedAsAToken() {
        var node = Assert.Single(Parse("{{$upper Name}}"));

        Assert.Equal("$upper", node.ActionText);

        var argument = Assert.Single(node.ArgumentList);

        Assert.Equal(TemplateActionType.MustacheToken, argument.Action);
        Assert.Equal("Name", argument.ActionText);
    }

    /// <summary>
    /// A quoted argument is a literal, not a property read. Confusing the two emits
    /// <c>model."text"</c>, which is the shape of defect that shipped in the web generator's named
    /// binding attributes.
    /// </summary>
    [Fact]
    public void AQuotedArgumentIsParsedAsAStringLiteral() {
        var node = Assert.Single(Parse("{{$upper \"literal\"}}"));

        var argument = Assert.Single(node.ArgumentList);

        Assert.Equal(TemplateActionType.StringLiteral, argument.Action);
        Assert.Equal("literal", argument.ActionText);
    }

    [Fact]
    public void SeveralArgumentsKeepTheirOrder() {
        var node = Assert.Single(Parse("{{$format First \"middle\" Last}}"));

        Assert.Equal(3, node.ArgumentList.Count);
        Assert.Equal("First", node.ArgumentList[0].ActionText);
        Assert.Equal("middle", node.ArgumentList[1].ActionText);
        Assert.Equal(TemplateActionType.StringLiteral, node.ArgumentList[1].Action);
        Assert.Equal("Last", node.ArgumentList[2].ActionText);
    }

    /// <summary>A quoted argument may contain spaces without splitting into two arguments.</summary>
    [Fact]
    public void AQuotedArgumentMayContainSpaces() {
        var node = Assert.Single(Parse("{{$upper \"two words\"}}"));

        var argument = Assert.Single(node.ArgumentList);

        Assert.Equal("two words", argument.ActionText);
    }

    /// <summary>
    /// The format-string form: a colon argument followed by a literal. The emitter looks for exactly
    /// this shape to decide whether to pass a format string to <c>FormatData</c>.
    /// </summary>
    [Fact]
    public void AFormatStringParsesAsAColonFollowedByALiteral() {
        var node = Assert.Single(Parse("{{Born : \"yyyy-MM-dd\"}}"));

        Assert.Equal("Born", node.ActionText);
        Assert.Equal(2, node.ArgumentList.Count);
        Assert.Equal(":", node.ArgumentList[0].ActionText);
        Assert.Equal(TemplateActionType.StringLiteral, node.ArgumentList[1].Action);
        Assert.Equal("yyyy-MM-dd", node.ArgumentList[1].ActionText);
    }

    [Fact]
    public void AnUnterminatedStringArgumentThrows() {
        var exception = Assert.Throws<Exception>(() => Parse("{{$upper \"unclosed}}"));

        Assert.Contains("Could not find end", exception.Message);
    }

    // ---------------------------------------------------------------- blocks

    [Fact]
    public void ABlockOpenAndCloseProduceOneBlockNode() {
        var node = Assert.Single(Parse("{{#each Tags}}{{/each}}"));

        Assert.Equal(TemplateActionType.Block, node.Action);
        Assert.Equal("each", node.ActionText);
    }

    [Fact]
    public void ABlocksCollectionArgumentIsOnTheBlockNode() {
        var node = Assert.Single(Parse("{{#each Tags}}{{/each}}"));

        var argument = Assert.Single(node.ArgumentList);

        Assert.Equal("Tags", argument.ActionText);
    }

    [Fact]
    public void ContentInsideABlockBecomesAChildOfTheBlock() {
        var node = Assert.Single(Parse("{{#each Tags}}<li>x</li>{{/each}}"));

        var child = Assert.Single(node.ChildNodes);

        Assert.Equal(TemplateActionType.Content, child.Action);
        Assert.Equal("<li>x</li>", child.ActionText);
    }

    [Fact]
    public void TokensInsideABlockBecomeChildrenOfTheBlock() {
        var node = Assert.Single(Parse("{{#each Tags}}<li>{{Label}}</li>{{/each}}"));

        Assert.Equal(3, node.ChildNodes.Count);
        Assert.Equal("Label", node.ChildNodes[1].ActionText);
    }

    /// <summary>
    /// A nested block hangs off its parent, not off the root. Flattening it would render the inner
    /// loop once instead of once per outer item.
    /// </summary>
    [Fact]
    public void ANestedBlockIsAChildOfTheOuterBlock() {
        var outer = Assert.Single(Parse("{{#each Tags}}{{#each Aliases}}x{{/each}}{{/each}}"));

        var inner = Assert.Single(outer.ChildNodes);

        Assert.Equal(TemplateActionType.Block, inner.Action);
        Assert.Equal("each", inner.ActionText);
        Assert.Equal("Aliases", Assert.Single(inner.ArgumentList).ActionText);
    }

    [Fact]
    public void SiblingBlocksBothLandAtTheRoot() {
        var nodes = Parse("{{#each Tags}}a{{/each}}{{#each Words}}b{{/each}}");

        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, node => Assert.Equal(TemplateActionType.Block, node.Action));
    }

    [Fact]
    public void AnIfBlockCarriesItsCondition() {
        var node = Assert.Single(Parse("{{#if Active}}on{{/if}}"));

        Assert.Equal("if", node.ActionText);
        Assert.Equal("Active", Assert.Single(node.ArgumentList).ActionText);
    }

    /// <summary><c>else</c> is a child of the <c>if</c>, not a block of its own.</summary>
    [Fact]
    public void AnElseIsAChildOfTheIfBlock() {
        var node = Assert.Single(Parse("{{#if Active}}on{{else}}off{{/if}}"));

        Assert.Contains(node.ChildNodes, child => child.ActionText == "else");
    }

    [Fact]
    public void AMismatchedClosingTagThrowsNamingBothTags() {
        var exception = Assert.Throws<Exception>(() => Parse("{{#each Tags}}x{{/if}}"));

        Assert.Contains("expected 'each'", exception.Message);
        Assert.Contains("found 'if'", exception.Message);
    }

    [Fact]
    public void AClosingTagWithNothingOpenThrowsNamingTheTag() {
        var exception = Assert.Throws<Exception>(() => Parse("x{{/each}}"));

        Assert.Contains("No open tag for each", exception.Message);
    }

    /// <summary>
    /// An unclosed block leaves the parser's stack non-empty and the block never reaches the output
    /// list, so the template's whole body disappears without an error. Recorded 2026-08-11: this is
    /// the one malformation that is not reported, and it surfaces later as a compile error against a
    /// template class the generator never wrote.
    /// </summary>
    [Fact]
    public void AnUnclosedBlockSilentlyProducesNoNodes() {
        Assert.Empty(Parse("{{#each Tags}}<li>x</li>"));
    }

    [Fact]
    public void AnUnterminatedTokenThrowsQuotingTheText() {
        var exception = Assert.Throws<Exception>(() => Parse("head{{Name"));

        Assert.Contains("Could not find end of mustache token", exception.Message);
        Assert.Contains("{{Name", exception.Message);
    }

    // ---------------------------------------------------------------- trim markers

    /// <summary>
    /// A tilde marks whitespace for removal and is not part of the token name. Leaving it on turns
    /// <c>~#each</c> into an unknown block that emits nothing.
    /// </summary>
    [Fact]
    public void ATrimMarkerIsNotPartOfTheTokenName() {
        var node = Assert.Single(Parse("{{~#each Tags~}}x{{~/each~}}"));

        Assert.Equal("each", node.ActionText);
    }

    [Fact]
    public void ATrimMarkerOnAPlainTokenIsNotPartOfTheName() {
        var node = Assert.Single(Parse("{{~Name~}}"));

        Assert.Equal("Name", node.ActionText);
    }

    [Fact]
    public void ATrimmedBlockRecordsAllFourTrimPositions() {
        var node = Assert.Single(Parse("{{~#each Tags~}}x{{~/each~}}"));

        Assert.Contains(TemplateActionNodeTrimAttribute.OpenStart, node.TrimAttributes);
        Assert.Contains(TemplateActionNodeTrimAttribute.OpenEnd, node.TrimAttributes);
        Assert.Contains(TemplateActionNodeTrimAttribute.CloseStart, node.TrimAttributes);
        Assert.Contains(TemplateActionNodeTrimAttribute.CloseEnd, node.TrimAttributes);
    }

    [Fact]
    public void AnUntrimmedBlockRecordsNoTrimPositions() {
        var node = Assert.Single(Parse("{{#each Tags}}x{{/each}}"));

        Assert.Empty(node.TrimAttributes);
    }

    // ---------------------------------------------------------------- declarations

    [Fact]
    public void AModelDeclarationCarriesTheTypeNameAsItsArgument() {
        var nodes = Parse("{{model TestApp.Person}}<p>x</p>");

        Assert.Equal("model", nodes[0].ActionText);
        Assert.Equal("TestApp.Person", Assert.Single(nodes[0].ArgumentList).ActionText);
    }

    [Fact]
    public void AUsingDeclarationCarriesTheNamespaceAsItsArgument() {
        var nodes = Parse("{{using System.Text}}<p>x</p>");

        Assert.Equal("using", nodes[0].ActionText);
        Assert.Equal("System.Text", Assert.Single(nodes[0].ArgumentList).ActionText);
    }

    [Fact]
    public void AnInjectDeclarationCarriesTypeAndVariableName() {
        var nodes = Parse("{{inject IThing thing}}<p>x</p>");

        Assert.Equal("inject", nodes[0].ActionText);
        Assert.Equal(2, nodes[0].ArgumentList.Count);
        Assert.Equal("IThing", nodes[0].ArgumentList[0].ActionText);
        Assert.Equal("thing", nodes[0].ArgumentList[1].ActionText);
    }

    // ---------------------------------------------------------------- token info

    /// <summary>
    /// The raw delimiters are derived from the plain ones by doubling the inner brace, which is what
    /// makes <c>{{{</c> and <c>}}}</c> the raw form of <c>{{</c> and <c>}}</c>.
    /// </summary>
    [Fact]
    public void RawDelimitersAreDerivedFromThePlainOnes() {
        var tokenInfo = new StringTokenNodeParser.TokenInfo("{{", "}}");

        Assert.Equal("{{", tokenInfo.StartToken);
        Assert.Equal("}}", tokenInfo.EndToken);
        Assert.Equal("{{{", tokenInfo.RawStartToken);
        Assert.Equal("}}}", tokenInfo.RawEndToken);
    }
}
