using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Hardened.SourceGeneration.Testing;

/// <summary>
/// Everything a generator run produced: the generated files, plus diagnostics from both the
/// generator itself and the compilation that includes its output.
/// </summary>
public class GeneratorResult(
    IReadOnlyDictionary<string, string> generatedSources,
    ImmutableArray<Diagnostic> generatorDiagnostics,
    ImmutableArray<Diagnostic> compilationDiagnostics,
    Compilation compilation,
    IReadOnlyList<Exception> generatorExceptions,
    IReadOnlyList<string>? duplicateHintNames = null) {

    public IReadOnlyDictionary<string, string> GeneratedSources { get; } = generatedSources;

    /// <summary>Diagnostics the generator reported — not errors in what it emitted.</summary>
    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; } = generatorDiagnostics;

    /// <summary>Diagnostics from compiling the input together with the generated trees.</summary>
    public ImmutableArray<Diagnostic> CompilationDiagnostics { get; } = compilationDiagnostics;

    public Compilation Compilation { get; } = compilation;

    public IReadOnlyList<Exception> GeneratorExceptions { get; } = generatorExceptions;

    /// <summary>Hint names emitted by more than one generator in the same run.</summary>
    public IReadOnlyList<string> DuplicateHintNames { get; } = duplicateHintNames ?? Array.Empty<string>();

    public IEnumerable<Diagnostic> Errors =>
        GeneratorDiagnostics.Concat(CompilationDiagnostics)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    /// <summary>The single generated file whose hint name contains <paramref name="fragment"/>.</summary>
    public string SourceContaining(string fragment) {
        var matches = GeneratedSources
            .Where(pair => pair.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(matches.Length > 0,
            $"No generated file matched '{fragment}'. Generated: {Describe()}");
        Assert.True(matches.Length == 1,
            $"'{fragment}' matched more than one generated file: {string.Join(", ", matches.Select(m => m.Key))}");

        return matches[0].Value;
    }

    /// <summary>
    /// Asserts the generator produced output that compiles cleanly.
    ///
    /// <para>
    /// Every generator test should end here. Asserting on an emitted string proves the generator
    /// produced the characters you expected; it does not prove a consumer can build.
    /// </para>
    /// </summary>
    public GeneratorResult AssertNoErrors() {
        Assert.True(GeneratorExceptions.Count == 0,
            "The generator threw:" + Environment.NewLine +
            string.Join(Environment.NewLine, GeneratorExceptions.Select(exception => $"  {exception}")));

        // A crash the generator caught itself. Hardened's generators wrap their emit in
        // SourceGeneratorWrapper, which turns an exception into a diagnostic - so the exception
        // never reaches GeneratorExceptions above, and the run looks orderly while having produced
        // nothing. Called out separately from the generic error check below because "the generator
        // crashed and emitted no code" and "the code it emitted does not compile" want very
        // different messages.
        var crashes = GeneratorDiagnostics
            .Where(diagnostic => diagnostic.Id == "HardenedException")
            .ToArray();

        Assert.True(crashes.Length == 0,
            "The generator caught an exception while emitting, so it produced " + Describe() + ":" +
            Environment.NewLine +
            string.Join(Environment.NewLine, crashes.Select(diagnostic => $"  {diagnostic.GetMessage()}")));

        Assert.True(DuplicateHintNames.Count == 0,
            "More than one generator emitted the same hint name, so one output overwrote another: " +
            string.Join(", ", DuplicateHintNames));

        var errors = Errors.ToArray();

        if (errors.Length > 0) {
            Assert.Fail(Describe(errors));
        }

        return this;
    }

    /// <summary>All generated files concatenated in a stable order, suitable for snapshotting.</summary>
    public string ToSnapshot() {
        var builder = new StringBuilder();

        foreach (var pair in GeneratedSources.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            builder.AppendLine($"// ---- {pair.Key} ----");
            builder.AppendLine(pair.Value.Replace("\r\n", "\n").TrimEnd());
            builder.AppendLine();
        }

        return builder.ToString().Replace("\r\n", "\n").TrimEnd() + "\n";
    }

    private string Describe() =>
        GeneratedSources.Count == 0
            ? "(the generator produced nothing)"
            : string.Join(", ", GeneratedSources.Keys.OrderBy(key => key, StringComparer.Ordinal));

    /// <summary>
    /// Whether <paramref name="span"/> points into the generated file <paramref name="hintName"/>.
    /// </summary>
    /// <remarks>
    /// The null check is the whole point. A diagnostic reported at <see cref="Location.None"/> has a
    /// null <c>Path</c>, and every diagnostic this codebase reports is at <c>Location.None</c> by
    /// policy — a syntax location would travel with the model through the incremental caches, which
    /// compare models for equality to decide whether to rerun. So the one case
    /// <see cref="Describe(IReadOnlyList{Diagnostic})"/> exists to explain — a generator reporting an
    /// error — was the one case that made it throw a <see cref="NullReferenceException"/> instead of
    /// printing anything.
    /// </remarks>
    private static bool IsIn(FileLinePositionSpan span, string hintName) =>
        span.Path?.EndsWith(hintName, StringComparison.Ordinal) == true;

    /// <summary>
    /// The failure message that matters. A compiler error with no source attached is unreadable, so
    /// every generated file carrying an error is printed with line numbers and the offending lines
    /// marked.
    /// </summary>
    private string Describe(IReadOnlyList<Diagnostic> errors) {
        var message = new StringBuilder()
            .AppendLine($"The generated code does not compile — {errors.Count} error(s).")
            .AppendLine();

        foreach (var error in errors) {
            var span = error.Location.GetLineSpan();
            var where = span.Path is { Length: > 0 }
                ? $"{Path.GetFileName(span.Path)}:{span.StartLinePosition.Line + 1}"
                : "no location";

            message.AppendLine($"  {error.Id} ({where}): {error.GetMessage()}");
        }

        foreach (var pair in GeneratedSources.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            var failingLines = errors
                .Select(error => error.Location.GetLineSpan())
                .Where(span => IsIn(span, pair.Key))
                .Select(span => span.StartLinePosition.Line)
                .ToHashSet();

            if (failingLines.Count == 0) {
                continue;
            }

            message
                .AppendLine()
                .AppendLine($"  ── {pair.Key} " + new string('─', Math.Max(0, 58 - pair.Key.Length)))
                .Append(WithLineNumbers(pair.Value, failingLines));
        }

        var located = errors.Any(error =>
            GeneratedSources.Keys.Any(hint => IsIn(error.Location.GetLineSpan(), hint)));

        if (!located) {
            message
                .AppendLine()
                .AppendLine(
                    "  No error landed in a generated file, so these are errors in the test's own " +
                    "source — usually a missing reference anchor rather than a generator defect.")
                .AppendLine($"  Generated: {Describe()}");
        }

        return message.ToString();
    }

    private static string WithLineNumbers(string source, IReadOnlySet<int> markedLines) {
        var text = SourceText.From(source);
        var builder = new StringBuilder();
        var width = text.Lines.Count.ToString().Length;

        foreach (var line in text.Lines) {
            builder
                .Append("  ")
                .Append(markedLines.Contains(line.LineNumber) ? '>' : ' ')
                .Append(' ')
                .Append((line.LineNumber + 1).ToString().PadLeft(width))
                .Append(" │ ")
                .AppendLine(line.ToString());
        }

        return builder.ToString();
    }
}

/// <summary>
/// The generated output of two consecutive generator runs, plus why the second run's outputs were
/// recomputed or reused.
/// </summary>
public class IncrementalRunResult(
    IReadOnlyDictionary<string, string> firstRun,
    IReadOnlyDictionary<string, string> secondRun,
    IReadOnlyList<IncrementalStepRunReason> outputReasons) {

    public IReadOnlyDictionary<string, string> FirstRun { get; } = firstRun;

    public IReadOnlyDictionary<string, string> SecondRun { get; } = secondRun;

    public IReadOnlyList<IncrementalStepRunReason> OutputReasons { get; } = outputReasons;

    /// <summary>
    /// True when the second run reused every cached output, meaning the edit was correctly
    /// recognised as irrelevant to generation.
    /// </summary>
    public bool AllOutputsCached =>
        OutputReasons.Count > 0 &&
        OutputReasons.All(reason =>
            reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged);
}
