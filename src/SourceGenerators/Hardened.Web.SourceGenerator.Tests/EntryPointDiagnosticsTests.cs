using Hardened.DependencyModules.SourceGenerator;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// Two Hardened entry points in one assembly compile, and until this diagnostic did so silently -
/// producing two routing tables over every handler in the compilation, two links types and two sets
/// of registrations, with nothing saying which entry point a host runs.
/// </summary>
/// <remarks>
/// Asserted over names rather than through a generator driver. A <c>SourceProductionContext</c>
/// cannot be constructed outside Roslyn, and running <c>HardenedSourceGenerator</c> from this
/// project would make every CSharpAuthor type ambiguous - both generator assemblies compile it in.
/// </remarks>
public class EntryPointDiagnosticsTests {

    [Fact]
    public void For_SaysNothingAboutOneEntryPoint() {
        Assert.Null(EntryPointDiagnostics.For(new[] { "Application" }));
    }

    /// <summary>
    /// An assembly with no entry point at all is an ordinary library, not a mistake.
    /// </summary>
    [Fact]
    public void For_SaysNothingAboutNone() {
        Assert.Null(EntryPointDiagnostics.For(System.Array.Empty<string>()));
    }

    [Fact]
    public void For_ReportsTwoAsAnError() {
        var diagnostic = EntryPointDiagnostics.For(new[] { "Application", "SecondApplication" });

        Assert.NotNull(diagnostic);
        Assert.Equal(EntryPointDiagnostics.DiagnosticId, diagnostic!.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>
    /// One report rather than one per surplus entry point: the problem is that there is more than
    /// one, not that any particular one is wrong.
    /// </summary>
    [Fact]
    public void For_NamesTwoOfThemInAStableOrder() {
        var message = EntryPointDiagnostics.For(
            new[] { "Zebra", "Application", "Middle" })!.GetMessage();

        Assert.Contains("'Application'", message);
        Assert.Contains("'Middle'", message);
        Assert.DoesNotContain("'Zebra'", message);
    }

    /// <summary>
    /// The order the entry points arrive in is the syntax provider's, which is not stable - so the
    /// pair named has to be chosen rather than taken.
    /// </summary>
    [Fact]
    public void For_NamesTheSamePairHoweverTheyArrive() {
        var one = EntryPointDiagnostics.For(new[] { "Beta", "Alpha", "Gamma" })!.GetMessage();
        var other = EntryPointDiagnostics.For(new[] { "Gamma", "Beta", "Alpha" })!.GetMessage();

        Assert.Equal(one, other);
    }

    /// <summary>
    /// The message has to say how to keep both deliberately, or the only way out of an error is to
    /// guess.
    /// </summary>
    [Fact]
    public void For_SaysHowToOptIn() {
        var message = EntryPointDiagnostics.For(new[] { "A", "B" })!.GetMessage();

        Assert.Contains("NoWarn", message);
        Assert.Contains("WebLibrary", message);
    }
}
