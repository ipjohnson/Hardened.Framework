using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The assertions <see cref="GeneratorResult.AssertNoErrors"/> cannot make on its own.
///
/// <para>
/// Discovered during the <c>web-routing</c> run on 2026-08-12 and recorded in
/// <c>TESTING-PLAN.md</c> §12.1: <c>SourceGeneratorWrapper.Wrap</c> catches every exception thrown
/// while emitting and reports it as a <c>HardenedException</c> diagnostic at
/// <see cref="DiagnosticSeverity.Warning"/>. <c>AssertNoErrors</c> filters on
/// <see cref="DiagnosticSeverity.Error"/> and <c>GeneratorExceptions</c> reads the driver's own
/// <c>Exception</c>, which the wrapper already swallowed — so a generator that blew up mid-emit
/// produces no output at all, no error, and a green <c>AssertNoErrors</c>.
/// </para>
///
/// <para>
/// A test that only calls <c>AssertNoErrors</c> is therefore green over zero generated files. Every
/// generator case in this suite pairs it with <see cref="AssertGenerated"/>, which insists the
/// wrapper reported nothing and that the files the case is about actually exist.
/// </para>
/// </summary>
internal static class GeneratorOutputAssertions {

    /// <summary>The id <c>SourceGeneratorWrapper</c> reports a swallowed emit-time crash under.</summary>
    private const string CrashDiagnosticId = "HardenedException";

    /// <summary>
    /// Asserts the wrapper caught nothing. A <c>HardenedException</c> means the generator threw
    /// while emitting, so whatever it was supposed to write is simply missing.
    /// </summary>
    internal static GeneratorResult AssertGeneratorDidNotCrash(this GeneratorResult result) {
        var crashes = result.GeneratorDiagnostics
            .Where(diagnostic => diagnostic.Id == CrashDiagnosticId)
            .ToArray();

        Assert.True(crashes.Length == 0,
            "The generator threw while emitting and the wrapper turned it into a warning, so it " +
            "produced nothing and reported success:" + Environment.NewLine +
            string.Join(Environment.NewLine, crashes.Select(crash => "  " + crash.GetMessage())) +
            Environment.NewLine + "Generated: " + Describe(result));

        return result;
    }

    /// <summary>
    /// Asserts the generator did not crash and that a file whose hint name contains each fragment
    /// was emitted. This is what makes a generator test able to fail when nothing was generated.
    /// </summary>
    internal static GeneratorResult AssertGenerated(
        this GeneratorResult result, params string[] hintNameFragments) {
        result.AssertGeneratorDidNotCrash();

        foreach (var fragment in hintNameFragments) {
            Assert.True(
                result.GeneratedSources.Keys.Any(
                    key => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
                $"Nothing was generated with '{fragment}' in its hint name. Generated: {Describe(result)}");
        }

        return result;
    }

    /// <summary>
    /// Asserts nothing was emitted whose hint name contains the fragment — used where the contract is
    /// that a construct is deliberately skipped rather than generated wrong.
    /// </summary>
    internal static GeneratorResult AssertNotGenerated(
        this GeneratorResult result, string hintNameFragment) {
        Assert.DoesNotContain(result.GeneratedSources.Keys,
            key => key.Contains(hintNameFragment, StringComparison.OrdinalIgnoreCase));

        return result;
    }

    /// <summary>
    /// Every hint name emitted except <c>_SpecModelDiagnostic.g.cs</c>, which is written on every run
    /// whatever the input and so churns any assertion that counts or snapshots the output.
    /// </summary>
    internal static IReadOnlyList<string> HintNamesExceptDiagnostic(this GeneratorResult result) =>
        result.GeneratedSources.Keys
            .Where(key => key != OpenApiGenerator.DiagnosticHintName)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

    private static string Describe(GeneratorResult result) =>
        result.GeneratedSources.Count == 0
            ? "(nothing)"
            : string.Join(", ", result.GeneratedSources.Keys.OrderBy(key => key, StringComparer.Ordinal));
}
