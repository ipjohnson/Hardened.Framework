using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// A function signature naming a type the compiler cannot resolve.
///
/// <para>
/// The web generator's <c>UnresolvableTypeTests</c> is the same suite for the same rule: one
/// unbindable signature costs its own handler and nothing else. The function generator reached
/// that rule later. Until then an unresolvable parameter threw out of the syntax transform, which
/// Roslyn reports as <c>CS8785</c> and which costs the whole assembly its generated code, and an
/// unattributed one got as far as being skipped while the provider went on naming the invoker that
/// was never emitted.
/// </para>
/// </summary>
public class FunctionUnresolvableTypeTests
{
    private static GeneratorResult Generate(string parameter, string extraTypes = "") =>
        FunctionGeneratorHarness.Generate(
            FunctionGeneratorHarness.Application(
                $$"""
                    [HardenedFunction]
                    public void Process({{parameter}}) { }

                    [HardenedFunction]
                    public void Healthy() { }
                """,
                extraTypes
            )
        );

    private static GeneratorResult Plain() => Generate("NotDeclaredAnywhere model");

    private static GeneratorResult Attributed() =>
        Generate(
            """[FromContext("id")] NotDeclaredAnywhere id""",
            FunctionGeneratorHarness.FromContextAttributeDeclaration
        );

    public static TheoryData<bool> BothForms => new() { true, false };

    private static GeneratorResult Result(bool attributed) => attributed ? Attributed() : Plain();

    [Theory]
    [MemberData(nameof(BothForms))]
    public void TheGeneratorDoesNotThrow(bool attributed)
    {
        Assert.Empty(Result(attributed).GeneratorExceptions);
    }

    /// <summary>
    /// The behaviour the whole rule is for. A crash out of the syntax transform took the healthy
    /// function down with the broken one, along with the dependency registration.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothForms))]
    public void EveryOtherFunctionInTheAssemblyStillGenerates(bool attributed)
    {
        Assert.Contains(Result(attributed).GeneratedSources.Keys, key => key.Contains("Healthy"));
    }

    [Theory]
    [MemberData(nameof(BothForms))]
    public void TheFunctionThatCouldNotBindIsNotGenerated(bool attributed)
    {
        Assert.DoesNotContain(
            Result(attributed).GeneratedSources.Keys,
            key => key.Contains("Process")
        );
    }

    /// <summary>
    /// The provider has to agree. Naming an invoker that was never emitted puts a type that does
    /// not exist in the provider and in the dependency registration, so the author is told their
    /// generated code is missing rather than that their parameter does not bind.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothForms))]
    public void TheProviderDoesNotNameTheFunctionThatWasSkipped(bool attributed)
    {
        var provider = Result(attributed).SourceContaining("FunctionHandlers.cs");

        Assert.DoesNotContain("TestFunctions_Process", provider);
        Assert.Contains("TestFunctions_Healthy", provider);
    }

    /// <summary>
    /// A warning, not an error, and once. The compiler already reports <c>CS0246</c> for the name
    /// itself, so this says only the part it cannot: that the function was dropped as a result.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothForms))]
    public void TheReasonIsReportedOnceAsAWarning(bool attributed)
    {
        var diagnostic = Assert.Single(
            Result(attributed).GeneratorDiagnostics,
            candidate => candidate.Id == UnresolvedHandler.DiagnosticId
        );

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Process", diagnostic.GetMessage());
    }

    /// <summary>
    /// Nothing is left in the generated code for the compiler to complain about. The one error is
    /// the <c>CS0246</c> the author caused.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothForms))]
    public void TheOnlyErrorIsTheOneTheAuthorCaused(bool attributed)
    {
        var error = Assert.Single(Result(attributed).Errors);

        Assert.Equal("CS0246", error.Id);
        Assert.Contains("NotDeclaredAnywhere", error.GetMessage());
    }
}
