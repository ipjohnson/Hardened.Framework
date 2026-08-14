using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Shared;

/// <summary>
/// How attribute arguments are spelled when copied into generated source.
///
/// <para>
/// A filter attribute's arguments are lifted verbatim from the consumer's file into a generated one
/// written with <c>TypeOutputMode.Global</c> and carrying none of the consumer's <c>using</c>
/// directives. An argument that relied on those usings — the natural spelling of an enum member —
/// therefore has to be rewritten to its <c>global::</c> form or it fails with CS0103 in code the
/// consumer never wrote and cannot edit.
/// </para>
///
/// <para>
/// Every case here is silent when it breaks: the generator succeeds, and the damage shows up as a
/// compile error in generated output or, worse, as an argument quietly emitted in the wrong
/// position. <see cref="AStringContainingAnEqualsStaysPositional"/> is the sharpest of those.
/// </para>
/// </summary>
public class AttributeArgumentQualificationTests {

    private const string Attributes = """
        using System;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [Flags]
        public enum AuditLevel {
            None = 0,
            Warning = 1,
            Error = 2
        }

        public static class AuditDefaults {
            public const string Source = "default-source";

            public static string Fallback => "fallback";
        }

        public class AuditAttribute : Attribute {
            public AuditAttribute() { }

            public AuditAttribute(string name) { Name = name; }

            public AuditAttribute(AuditLevel level) { Level = level; }

            public string? Name { get; set; }

            public AuditLevel Level { get; set; }

            public Type? Target { get; set; }
        }
        """;

    private static string WithAttributes(string controller) =>
        Attributes + Environment.NewLine + controller;

    private static string GenerateHandler(string attribute) =>
        RequestGeneratorHarness.Generate(WithAttributes($$"""
            public class OrderController {
                [Get("/orders")]
                {{attribute}}
                public string All() => "x";
            }
            """)).AssertNoErrors().SourceContaining("All");

    /// <summary>
    /// The case the rewriter exists for: <c>AuditLevel.Warning</c> resolves in the consumer's file
    /// through its usings and resolves nowhere at all in the generated one.
    /// </summary>
    [Fact]
    public void AnEnumMemberIsQualified() {
        var source = GenerateHandler("[Audit(Level = AuditLevel.Warning)]");

        Assert.Contains("global::TestApp.AuditLevel.Warning", source);
    }

    /// <summary>
    /// Resolved through the semantic model rather than folded to a constant. An enum's constant
    /// value is its underlying integer, so folding would emit <c>3</c> — which needs a cast to
    /// assign back to the property and reads as nothing.
    /// </summary>
    [Fact]
    public void AFlagCombinationKeepsBothMemberNames() {
        var source = GenerateHandler("[Audit(Level = AuditLevel.Warning | AuditLevel.Error)]");

        Assert.Contains("global::TestApp.AuditLevel.Warning", source);
        Assert.Contains("global::TestApp.AuditLevel.Error", source);
    }

    [Fact]
    public void AConstIsQualified() {
        var source = GenerateHandler("[Audit(Name = AuditDefaults.Source)]");

        Assert.Contains("global::TestApp.AuditDefaults.Source", source);
    }

    /// <summary>
    /// The operand of a <c>typeof</c> is a type, and Roslyn casts the rewritten operand back to
    /// <c>TypeSyntax</c>. Qualifying it as an expression produced a
    /// <c>MemberAccessExpressionSyntax</c>, which prints identically and threw
    /// <c>InvalidCastException</c> out of the rewriter — taking down the whole generator, so a
    /// single <c>typeof</c> argument anywhere failed the consumer's build with a stack trace
    /// rather than a diagnostic.
    /// </summary>
    [Fact]
    public void ATypeOfArgumentDoesNotCrashTheGenerator() {
        var source = GenerateHandler("[Audit(Target = typeof(AuditAttribute))]");

        Assert.Contains("typeof(global::TestApp.AuditAttribute)", source);
    }

    /// <summary>
    /// <c>nameof</c> evaluates to the source spelling of its argument, so qualifying inside it
    /// changes the result to nothing and only makes the output harder to read.
    /// </summary>
    [Fact]
    public void NameofIsLeftAlone() {
        var source = GenerateHandler("[Audit(Name = nameof(AuditAttribute))]");

        Assert.Contains("nameof(AuditAttribute)", source);
    }

    /// <summary>
    /// The trailing half of a name binds to the same symbol as the whole, so qualifying it in place
    /// would produce <c>A.global::A.B</c>.
    /// </summary>
    [Fact]
    public void AQualifiedNameIsNotQualifiedTwice() {
        var source = GenerateHandler("[Audit(Level = AuditLevel.Warning)]");

        Assert.DoesNotContain("global::TestApp.global::", source);
        Assert.DoesNotContain(".global::", source);
    }

    /// <summary>
    /// A positional argument stays positional. The distinction is drawn syntactically — on
    /// <c>NameEquals</c> — rather than by looking for an "=" in the argument text, and this is the
    /// case that tells the two apart: a string that merely contains one must not become a property
    /// initializer the attribute does not have.
    /// </summary>
    [Fact]
    public void AStringContainingAnEqualsStaysPositional() {
        var source = GenerateHandler("""[Audit("a=b")]""");

        Assert.Contains("""new global::TestApp.AuditAttribute("a=b")""", source);
    }

    [Fact]
    public void APositionalEnumArgumentIsQualified() {
        var source = GenerateHandler("[Audit(AuditLevel.Error)]");

        Assert.Contains("new global::TestApp.AuditAttribute(global::TestApp.AuditLevel.Error)", source);
    }

    /// <summary>Named-argument syntax — <c>parameter: value</c> — keeps its label.</summary>
    [Fact]
    public void ANamedArgumentKeepsItsLabel() {
        var source = GenerateHandler("[Audit(level: AuditLevel.Error)]");

        Assert.Contains("level: global::TestApp.AuditLevel.Error", source);
    }

    /// <summary>
    /// A literal binds to no symbol, so the rewriter has nothing to qualify and must copy it
    /// through rather than drop it.
    /// </summary>
    [Fact]
    public void ALiteralIsCopiedThrough() {
        var source = GenerateHandler("""[Audit("plain")]""");

        Assert.Contains("""new global::TestApp.AuditAttribute("plain")""", source);
    }

    /// <summary>
    /// A property initializer and a constructor argument in one attribute, which is where emitting
    /// either in the other's position compiles to something wrong rather than failing.
    /// </summary>
    [Fact]
    public void AConstructorArgumentAndAPropertyAreEmittedSeparately() {
        var source = GenerateHandler("""[Audit("ctor", Level = AuditLevel.Error)]""");

        Assert.Contains("""new global::TestApp.AuditAttribute("ctor")""", source);
        Assert.Contains("Level = global::TestApp.AuditLevel.Error", source);
    }
}
