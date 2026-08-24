using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// Every file the attribute-routed pipeline emits, byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RouteTableGoldenTests"/> guards one generator's output. This guards the whole
/// pipeline — handler classes, parameter binding, the routing table, links, the OpenAPI document —
/// because the work it exists for changes the head of that pipeline rather than a leaf of it.
/// Code-first is to stop building <c>RequestHandlerModel</c> directly and go through
/// <c>ServiceSpecModel</c> like the described front-ends, and the property that makes that safe is
/// that not one emitted byte moves.
/// </para>
/// <para>
/// A failure here is not a signal to re-record. Record only when the emitted change is the point of
/// the commit, and say so in the commit message. Set <c>HARDENED_RECORD_FIXTURES=1</c> to do it.
/// </para>
/// </remarks>
public class WebPipelineGoldenTests {
    private static readonly bool Recording =
        Environment.GetEnvironmentVariable("HARDENED_RECORD_FIXTURES") == "1";

    private static string FixtureRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "Hardened.SourceGenerator.Tests.csproj"))) {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return Path.Combine(directory!.FullName, "Web", "Fixtures", "WebPipeline");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Pipeline_EmitsByteIdenticalSources(string scenario) {
        var result = RequestGeneratorHarness.Generate(WebPipelineCorpus.Source(scenario));

        result.AssertNoErrors();

        var directory = Path.Combine(FixtureRoot(), scenario);

        if (Recording) {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }

            Directory.CreateDirectory(directory);

            foreach (var source in result.GeneratedSources) {
                File.WriteAllText(Path.Combine(directory, Sanitize(source.Key)), source.Value);
            }

            return;
        }

        Assert.True(Directory.Exists(directory),
            $"No fixtures for '{scenario}' at {directory}. Record them deliberately, then review the diff.");

        // GetFileName is string?-returning, which only matters under TreatWarningsAsErrors - so a
        // plain test run is green and CI is not.
        var recorded = Directory.GetFiles(directory)
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText, StringComparer.Ordinal);

        var emitted = result.GeneratedSources
            .ToDictionary(source => Sanitize(source.Key), source => source.Value, StringComparer.Ordinal);

        // Named separately from the content comparison: a file appearing or disappearing is a
        // different kind of change from one whose contents moved, and reads very differently in a
        // failure message.
        Assert.Equal(
            recorded.Keys.OrderBy(name => name, StringComparer.Ordinal),
            emitted.Keys.OrderBy(name => name, StringComparer.Ordinal));

        foreach (var pair in recorded.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            Assert.Equal(pair.Value, emitted[pair.Key]);
        }
    }

    /// <summary>Hint names carry characters a file name cannot.</summary>
    private static string Sanitize(string hintName) =>
        string.Join("_", hintName.Split(Path.GetInvalidFileNameChars()));

    public static TheoryData<string> Corpus() {
        var data = new TheoryData<string>();

        foreach (var scenario in WebPipelineCorpus.Scenarios) {
            data.Add(scenario);
        }

        return data;
    }
}
