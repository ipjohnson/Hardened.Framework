using Hardened.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// What happens when a handler signature names a type the compiler cannot resolve.
///
/// <para>
/// It crashes the generator, and the generator then contributes <em>nothing to the whole
/// assembly</em> — not just the handler that named the type. Every route, every parameter bag and
/// the dependency registration all disappear together.
/// </para>
///
/// <para>
/// <c>BaseRequestModelGenerator.GetParameterInfo</c> resolves the parameter with
/// <c>parameter.Type?.GetTypeDefinition(context)!</c>. The <c>?.</c> is honest — resolution does
/// return null for a name that does not exist — and the <c>!</c> then suppresses the warning that
/// says so. The null walks past the <c>Equals</c> checks, which tolerate it, and dereferences at
/// the first <c>parameterType.TypeDefinitionEnum</c>.
/// </para>
///
/// <para>
/// This escapes <c>SourceGeneratorWrapper</c>, which catches emit-time exceptions: the crash is in
/// the syntax-provider transform, upstream of it. Roslyn reports it as <c>CS8785</c> at
/// <em>warning</em> severity, so a build without <c>TreatWarningsAsErrors</c> continues, produces
/// no generated code, and reports a cascade of errors about types that should have been generated.
/// </para>
///
/// <para>
/// A compiling project cannot reach this — the unresolvable name is a compile error of the user's
/// own. It bites in the editor, where a signature is briefly invalid on the way to being valid:
/// mid-rename, or before the model class is written. Recorded 2026-08-12 as observed behaviour,
/// not asserted as intended.
/// </para>
/// </summary>
public class UnresolvableTypeTests {

    private static GeneratorResultShape Run() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Post("/orders")]
                public string Save(NotDeclaredAnywhere model) => "";
            """));

        return new GeneratorResultShape(
            result.GeneratorExceptions.Count,
            result.GeneratedSources.Count,
            result.GeneratorDiagnostics);
    }

    private record GeneratorResultShape(
        int Exceptions,
        int GeneratedFiles,
        IReadOnlyList<Diagnostic> Diagnostics);

    [Fact]
    public void AnUnresolvableParameterTypeCrashesTheGenerator() {
        Assert.Equal(1, Run().Exceptions);
    }

    /// <summary>
    /// The part that costs: nothing is emitted for the assembly, including the handlers that were
    /// perfectly well formed.
    /// </summary>
    [Fact]
    public void TheCrashCostsTheWholeAssemblyItsGeneratedCode() {
        Assert.Equal(0, Run().GeneratedFiles);
    }

    /// <summary>
    /// Reported as a warning, not an error. Under <c>ContinuousIntegrationBuild=true</c> this
    /// repository turns it into one; a consumer that does not will build on and see only the
    /// downstream damage.
    /// </summary>
    [Fact]
    public void TheCrashIsReportedAsAWarningRatherThanAnError() {
        var generatorFailed = Assert.Single(
            Run().Diagnostics.Where(diagnostic => diagnostic.Id == "CS8785"));

        Assert.Equal(DiagnosticSeverity.Warning, generatorFailed.Severity);
    }
}
