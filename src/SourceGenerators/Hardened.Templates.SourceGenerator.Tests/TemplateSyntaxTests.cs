namespace Hardened.Templates.SourceGenerator.Tests;

/// <summary>
/// What each piece of template syntax turns into.
///
/// <para>
/// <see cref="GeneratedTemplateCompilesTests"/> proves the output builds; these prove it says the
/// right thing. Both matter — code that compiles and writes the wrong property is a defect a
/// compilation check cannot see. Every case here still calls
/// <see cref="Hardened.SourceGeneration.Testing.GeneratorResult.AssertNoErrors"/> first, because an
/// assertion about a string in a file that does not compile is worth nothing.
/// </para>
/// </summary>
public class TemplateSyntaxTests {

    /// <summary>
    /// <c>{{model}}</c> casts the incoming <c>object</c> once, up front. Everything after it reads
    /// properties off that local, which is what makes the rest of the template reflection-free.
    /// </summary>
    [Fact]
    public void ModelDeclarationCastsTheRequestValueOnce() {
        var template = TemplateGeneration
            .Generate(("Cast.html", "{{model TestApp.Person}}<p>{{Name}}</p>"))
            .AssertNoErrors()
            .TemplateClass("Cast");

        Assert.Contains("var model = (TestApp.Person)requestValue;", template);
    }

    /// <summary>A property token reads the property directly, not through a dictionary or reflection.</summary>
    [Fact]
    public void APropertyTokenReadsThePropertyDirectly() {
        var template = TemplateGeneration
            .Generate(("Direct.html", "{{model TestApp.Person}}<p>{{Name}}</p>"))
            .AssertNoErrors()
            .TemplateClass("Direct");

        Assert.Contains("model.Name", template);
    }

    /// <summary>
    /// The property name travels alongside the value so a formatter can key on it. Losing it would
    /// make every per-property format provider a no-op.
    /// </summary>
    [Fact]
    public void APropertyTokenPassesItsNameToTheFormatter() {
        var template = TemplateGeneration
            .Generate(("Named.html", "{{model TestApp.Person}}<p>{{Name}}</p>"))
            .AssertNoErrors()
            .TemplateClass("Named");

        Assert.Contains("FormatData(executionContext, \"Name\", model.Name", template);
    }

    /// <summary>
    /// A format string after the colon reaches <c>FormatData</c> as its format argument; without one
    /// the argument is <c>null</c> and the default formatter applies.
    /// </summary>
    [Fact]
    public void AFormatStringIsPassedThroughToTheFormatter() {
        var template = TemplateGeneration
            .Generate(("Format.html", "{{model TestApp.Person}}<p>{{Born : \"yyyy-MM-dd\"}}</p>"))
            .AssertNoErrors()
            .TemplateClass("Format");

        Assert.Contains("FormatData(executionContext, \"Born\", model.Born, \"yyyy-MM-dd\")", template);
    }

    [Fact]
    public void ATokenWithNoFormatStringPassesNull() {
        var template = TemplateGeneration
            .Generate(("NoFormat.html", "{{model TestApp.Person}}<p>{{Born}}</p>"))
            .AssertNoErrors()
            .TemplateClass("NoFormat");

        Assert.Contains("FormatData(executionContext, \"Born\", model.Born, null)", template);
    }

    /// <summary>
    /// Double braces escape, triple braces do not. This is the whole of the engine's XSS defence at
    /// the template level: <c>Write</c> runs the value through the extension's escape service,
    /// <c>WriteRaw</c> does not.
    /// </summary>
    [Fact]
    public void ADoubleBraceTokenIsWrittenThroughTheEscapeService() {
        var template = TemplateGeneration
            .Generate(("Escaped.html", "{{model TestApp.Person}}<p>{{Name}}</p>"))
            .AssertNoErrors()
            .TemplateClass("Escaped");

        Assert.Contains("writer.Write(", template);
        Assert.DoesNotContain("writer.WriteRaw(_services.DataFormattingService", template);
    }

    /// <summary>The unescaped form. See <c>ADoubleBraceTokenIsWrittenThroughTheEscapeService</c>.</summary>
    [Fact]
    public void ATripleBraceTokenBypassesTheEscapeService() {
        var template = TemplateGeneration
            .Generate(("Unescaped.html", "{{model TestApp.Person}}<p>{{{Name}}}</p>"))
            .AssertNoErrors()
            .TemplateClass("Unescaped");

        Assert.Contains("writer.WriteRaw(_services.DataFormattingService", template);
    }

    /// <summary>
    /// Literal content becomes a <c>static readonly</c> field rather than a literal at the call
    /// site, so the string is allocated once for the life of the process instead of per render.
    /// </summary>
    [Fact]
    public void LiteralContentBecomesAStaticReadonlyField() {
        var template = TemplateGeneration
            .Generate(("Content.html", "{{model TestApp.Person}}<h1>hello</h1>{{Name}}"))
            .AssertNoErrors()
            .TemplateClass("Content");

        Assert.Contains("private readonly static string _contentField", template);
        Assert.Contains("@\"<h1>hello</h1>\"", template);
    }

    /// <summary>
    /// Content is emitted as a verbatim string, so an embedded quote is doubled. Emitting it raw
    /// would terminate the literal and the generated file would not parse.
    /// </summary>
    [Fact]
    public void AQuoteInContentIsDoubledInTheVerbatimLiteral() {
        var template = TemplateGeneration
            .Generate(("Quote.html", "{{model TestApp.Person}}<a title=\"t\">{{Name}}</a>"))
            .AssertNoErrors()
            .TemplateClass("Quote");

        Assert.Contains("@\"<a title=\"\"t\"\">\"", template);
    }

    /// <summary><c>{{using}}</c> becomes a namespace import on the generated file.</summary>
    [Fact]
    public void AUsingDeclarationBecomesANamespaceImport() {
        var template = TemplateGeneration
            .Generate(("Import.html", "{{model Person}}{{using TestApp}}<p>{{Name}}</p>"))
            .AssertNoErrors()
            .TemplateClass("Import");

        Assert.Contains("using TestApp;", template);
    }

    /// <summary>Several <c>{{using}}</c> declarations in a row are all consumed.</summary>
    [Fact]
    public void EveryUsingDeclarationBecomesANamespaceImport() {
        var template = TemplateGeneration
            .Generate(("Imports.html",
                "{{model Person}}{{using TestApp}}{{using System.Text}}<p>{{Name}}</p>"))
            .AssertNoErrors()
            .TemplateClass("Imports");

        Assert.Contains("using TestApp;", template);
        Assert.Contains("using System.Text;", template);
    }

    /// <summary><c>{{inject}}</c> resolves from the request provider, not the root one.</summary>
    [Fact]
    public void AnInjectDeclarationResolvesFromTheRequestServiceProvider() {
        var template = TemplateGeneration
            .Generate(("Inject.html",
                "{{model TestApp.Person}}{{inject TestApp.Tag tag}}<p>{{Name}}</p>"))
            .AssertNoErrors()
            .TemplateClass("Inject");

        Assert.Contains("var tag = serviceProvider.GetRequiredService<TestApp.Tag>();", template);
    }

    /// <summary><c>{{#each}}</c> becomes a real <c>foreach</c> over the named collection.</summary>
    [Fact]
    public void AnEachBlockBecomesAForeachOverTheCollection() {
        var template = TemplateGeneration
            .Generate(("Loop.html",
                "{{model TestApp.Person}}{{#each Tags}}<li>{{Label}}</li>{{/each}}"))
            .AssertNoErrors()
            .TemplateClass("Loop");

        Assert.Contains("foreach(var eachVariable1 in model.Tags)", template);
    }

    /// <summary>
    /// Inside a loop the context variable is the loop item, so a token names a property of the item
    /// rather than of the model. Getting this wrong renders the outer model N times.
    /// </summary>
    [Fact]
    public void TokensInsideAnEachBindToTheLoopItem() {
        var template = TemplateGeneration
            .Generate(("Item.html",
                "{{model TestApp.Person}}{{#each Tags}}<li>{{Label}}</li>{{/each}}"))
            .AssertNoErrors()
            .TemplateClass("Item");

        Assert.Contains("eachVariable1.Label", template);
        Assert.DoesNotContain("model.Label", template);
    }

    /// <summary>
    /// Sibling loops get distinct variable names. Reusing one would redeclare it in the same scope,
    /// which is a compile error in the consuming project.
    /// </summary>
    [Fact]
    public void SiblingEachBlocksGetDistinctLoopVariables() {
        var template = TemplateGeneration
            .Generate(("Siblings.html",
                "{{model TestApp.Person}}{{#each Tags}}<li>{{Label}}</li>{{/each}}" +
                "{{#each Tags}}<li>{{Label}}</li>{{/each}}"))
            .AssertNoErrors()
            .TemplateClass("Siblings");

        Assert.Contains("eachVariable1", template);
        Assert.Contains("eachVariable2", template);
    }

    /// <summary>A nested loop binds to the inner item, and the outer variable stays reachable.</summary>
    [Fact]
    public void ANestedEachBindsToTheInnerItem() {
        var template = TemplateGeneration
            .Generate(("Deep.html",
                "{{model TestApp.Person}}{{#each Tags}}{{#each Aliases}}<i>{{Length}}</i>{{/each}}{{/each}}"))
            .AssertNoErrors()
            .TemplateClass("Deep");

        Assert.Contains("foreach(var eachVariable1 in model.Tags)", template);
        Assert.Contains("foreach(var eachVariable2 in eachVariable1.Aliases)", template);
        Assert.Contains("eachVariable2.Length", template);
    }

    /// <summary>
    /// <c>{{#if}}</c> delegates truthiness to <c>IBooleanLogicService</c> rather than emitting a raw
    /// <c>if (x)</c>, which is what lets a non-empty string or a non-empty collection count as true.
    /// </summary>
    [Fact]
    public void AnIfBlockDelegatesTruthinessToTheBooleanLogicService() {
        var template = TemplateGeneration
            .Generate(("Cond.html", "{{model TestApp.Person}}{{#if Active}}on{{/if}}"))
            .AssertNoErrors()
            .TemplateClass("Cond");

        Assert.Contains("if (_services.BooleanLogicService.IsTrueValue(model.Active))", template);
    }

    [Fact]
    public void AnElseBranchBecomesAnElseBlock() {
        var template = TemplateGeneration
            .Generate(("Else.html", "{{model TestApp.Person}}{{#if Active}}on{{else}}off{{/if}}"))
            .AssertNoErrors()
            .TemplateClass("Else");

        Assert.Contains("else", template);
    }

    [Fact]
    public void AnElseIfBranchBecomesAnElseIfBlock() {
        var template = TemplateGeneration
            .Generate(("ElseIf.html",
                "{{model TestApp.Person}}{{#if Active}}a{{else if Age}}b{{else}}c{{/if}}"))
            .AssertNoErrors()
            .TemplateClass("ElseIf");

        Assert.Contains("else if (_services.BooleanLogicService.IsTrueValue(model.Age))", template);
    }

    /// <summary>An <c>if</c> inside a loop tests the loop item, not the model.</summary>
    [Fact]
    public void AnIfInsideAnEachTestsTheLoopItem() {
        var template = TemplateGeneration
            .Generate(("CondItem.html",
                "{{model TestApp.Person}}{{#each Tags}}{{#if Label}}<li>{{Label}}</li>{{/if}}{{/each}}"))
            .AssertNoErrors()
            .TemplateClass("CondItem");

        Assert.Contains("IsTrueValue(eachVariable1.Label)", template);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// A helper token is resolved once, at construction, and cached in a field — not looked up per
    /// render. The lookup goes through <c>ITemplateHelperService</c> by token name.
    /// </summary>
    [Fact]
    public void AHelperTokenIsResolvedOnceIntoAField() {
        var template = TemplateGeneration
            .Generate(("Helper.html", "{{model TestApp.Person}}{{$String.ToUpper Name}}"))
            .AssertNoErrors()
            .TemplateClass("Helper");

        Assert.Contains("LocateHelper(\"String.ToUpper\")", template);
        Assert.Contains("_helper_String_ToUpper", template);
    }

    /// <summary>A helper token forces the generated <c>Execute</c> to become <c>async</c>.</summary>
    [Fact]
    public void AHelperTokenMakesTheExecuteMethodAsync() {
        var template = TemplateGeneration
            .Generate(("Async.html", "{{model TestApp.Person}}{{$String.ToUpper Name}}"))
            .AssertNoErrors()
            .TemplateClass("Async");

        Assert.Contains("async", template);
        Assert.Contains("await _helper_String_ToUpper(serviceProvider).Execute(executionContext", template);
    }

    /// <summary>A template with no helper stays synchronous and returns a completed task.</summary>
    [Fact]
    public void ATemplateWithNoHelperStaysSynchronous() {
        var template = TemplateGeneration
            .Generate(("Sync.html", "{{model TestApp.Person}}<p>{{Name}}</p>"))
            .AssertNoErrors()
            .TemplateClass("Sync");

        Assert.Contains("return Task.CompletedTask;", template);
    }

    /// <summary>
    /// Helper arguments arrive positionally after the execution context. A property argument becomes
    /// a property read; a quoted argument becomes a string literal.
    /// </summary>
    [Fact]
    public void HelperArgumentsArePassedPositionallyAfterTheContext() {
        var template = TemplateGeneration
            .Generate(("Args.html", "{{model TestApp.Person}}{{$String.Replace Name \"a\" \"b\"}}"))
            .AssertNoErrors()
            .TemplateClass("Args");

        Assert.Contains("Execute(executionContext, model.Name, \"a\", \"b\")", template);
    }

    /// <summary>A helper with no arguments still receives the execution context.</summary>
    [Fact]
    public void AHelperWithNoArgumentsStillReceivesTheContext() {
        var template = TemplateGeneration
            .Generate(("NoArgs.html", "{{model TestApp.Person}}{{$String.ToUpper}}"))
            .AssertNoErrors()
            .TemplateClass("NoArgs");

        Assert.Contains("Execute(executionContext)", template);
    }

    /// <summary>Inside a loop a helper argument binds to the loop item.</summary>
    [Fact]
    public void AHelperArgumentInsideAnEachBindsToTheLoopItem() {
        var template = TemplateGeneration
            .Generate(("HelperLoop.html",
                "{{model TestApp.Person}}{{#each Tags}}{{$String.ToUpper Label}}{{/each}}"))
            .AssertNoErrors()
            .TemplateClass("HelperLoop");

        Assert.Contains("Execute(executionContext, eachVariable1.Label)", template);
    }

    /// <summary>
    /// The same helper token used twice resolves into one field, not two. A second field would be a
    /// second lookup for no benefit; two fields with the same name would not compile.
    /// </summary>
    [Fact]
    public void TheSameHelperTokenResolvesIntoASingleField() {
        var template = TemplateGeneration
            .Generate(("Shared.html",
                "{{model TestApp.Person}}{{$String.ToUpper Name}}{{$String.ToUpper Nickname}}"))
            .AssertNoErrors()
            .TemplateClass("Shared");

        Assert.Equal(1, CountOf(template, "LocateHelper(\"String.ToUpper\")"));
    }

    /// <summary>
    /// A dot in a helper token is not legal in a field name, so it is replaced. <c>String.ToUpper</c>
    /// becoming <c>_helper_String.ToUpper</c> would not compile.
    /// </summary>
    [Fact]
    public void ADotInAHelperTokenIsSanitisedOutOfTheFieldName() {
        var template = TemplateGeneration
            .Generate(("Sanitise.html", "{{model TestApp.Person}}{{$Url.Encode Name}}"))
            .AssertNoErrors()
            .TemplateClass("Sanitise");

        Assert.Contains("_helper_Url_Encode", template);
        Assert.DoesNotContain("_helper_Url.Encode", template);
    }

    // ---------------------------------------------------------------- escaping

    /// <summary>
    /// The escape service is chosen by extension at construction and swapped onto the writer for the
    /// duration of the render, then put back. A template that leaked its escape service would change
    /// how the <em>calling</em> template escapes everything after the partial.
    /// </summary>
    [Theory]
    [InlineData("html")]
    [InlineData("js")]
    [InlineData("css")]
    [InlineData("md")]
    public void EachExtensionSelectsItsOwnEscapeService(string extension) {
        var template = TemplateGeneration
            .Generate(($"Escape.{extension}", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateClass("Escape");

        Assert.Contains($"GetEscapeService(\".{extension}\")", template);
    }

    /// <summary>The writer's escape service is restored after the template finishes.</summary>
    [Fact]
    public void TheWritersEscapeServiceIsRestoredAfterRendering() {
        var template = TemplateGeneration
            .Generate(("Restore.html", "{{model TestApp.Person}}{{Name}}"))
            .AssertNoErrors()
            .TemplateClass("Restore");

        Assert.Contains("var currentEscapeStringService = writer.EscapeService;", template);
        Assert.Contains("writer.EscapeService = _stringEscapeService;", template);
        Assert.Contains("writer.EscapeService = currentEscapeStringService;", template);
    }

    private static int CountOf(string haystack, string needle) {
        var count = 0;

        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal)) {
            count++;
        }

        return count;
    }
}
