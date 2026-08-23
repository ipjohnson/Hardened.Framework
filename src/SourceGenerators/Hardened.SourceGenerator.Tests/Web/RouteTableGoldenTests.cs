using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// Byte-level regression guard over the attribute-routed table's emitted C#.
///
/// <para>
/// This exists for the route tree unification: the spec table is being folded into this generator,
/// and the property that makes that merge safe is that <em>this</em> path's output does not move at
/// all. A behavioural suite cannot give that guarantee — it passes equally well against a table
/// that emits different code with the same observable routing.
/// </para>
///
/// <para>
/// A failure here is not a signal to re-record the fixture. Re-recording to make the build pass is
/// the same as deleting the test. Record only when the emitted change is the point of the commit,
/// and say so in the commit message.
/// </para>
/// </summary>
public class RouteTableGoldenTests {
    /// <summary>
    /// Set to true, run the suite once, and every fixture is rewritten from current output. Must
    /// never be committed as true — <see cref="RecordingIsOff"/> fails the build if it is.
    /// </summary>
    private const bool Recording = false;

    private static string FixtureDirectory() {
        // The fixtures live beside the source rather than in the output directory: they are
        // reviewed in a diff, which only works if they are in the tree the diff covers.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Hardened.SourceGenerator.Tests.csproj"))) {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return Path.Combine(directory!.FullName, "Web", "Fixtures", "RouteTable");
    }

    [Fact]
    public void RecordingIsOff() {
        // Recording left on would make every other test in this class assert nothing at all, and
        // it would do it silently.
        Assert.False(Recording, "Recording must be false in committed source.");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void RouteTable_OutputIsByteIdentical(string scenario) {
        var (appModel, handlers) = RouteCorpus.Build(scenario);

        var generated = RoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, CancellationToken.None);

        var path = Path.Combine(FixtureDirectory(), scenario + ".cs");

        if (Recording) {
            Directory.CreateDirectory(FixtureDirectory());
            File.WriteAllText(path, generated);
            return;
        }

        Assert.True(File.Exists(path),
            $"No fixture for '{scenario}' at {path}. Record it deliberately, then review the diff.");

        Assert.Equal(File.ReadAllText(path), generated);
    }

    public static TheoryData<string> Corpus() {
        var data = new TheoryData<string>();

        foreach (var scenario in RouteCorpus.Scenarios) {
            data.Add(scenario);
        }

        return data;
    }
}
