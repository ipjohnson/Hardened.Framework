using System.Reflection;
using Hardened.Web.StaticContent;
using Hardened.Web.StaticContent.BuildTask;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Hardened.Web.StaticContent.BuildTask.Tests;

/// <summary>
/// That the manifest the task writes actually compiles, and means what it says.
///
/// <para>
/// This is the assertion the rest of the emitter tests rest on. They check the text; this checks
/// that the text is C#, that it binds against the runtime package, and that loading it back
/// produces the entries the scan found. A generator whose output does not compile is a defect that
/// surfaces in somebody else's project, pointing at code they never wrote - which
/// <c>docs/TESTING-PLAN.md</c> §2.1 records as a thing that has already happened here twice.
/// </para>
/// </summary>
public class GeneratedManifestCompilesTests : IDisposable {

    private readonly string _root;

    public GeneratedManifestCompilesTests() {
        _root = Path.Combine(Path.GetTempPath(), "hardened-compile-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);
    }

    public void Dispose() {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }

        GC.SuppressFinalize(this);
    }

    private void Write(string relative, string content) {
        var path = Path.Combine(_root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Compiles <paramref name="source"/> against everything loaded here, which includes the
    /// runtime package and DependencyModules - the two things the emitted file binds to.
    /// </summary>
    private static Assembly Compile(string source) {
        // Both sources, because neither is enough on its own. Loaded assemblies bring the
        // framework, which lives in the shared runtime directory and never appears beside the
        // test; the output directory brings DependencyModules.Runtime, which nothing here touches
        // and so is never loaded. Missing either produces a wall of errors about the emitted file
        // that have nothing to do with it.
        var paths = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => assembly.Location)
            .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var references = paths
            .Select(TryReference)
            .OfType<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            "GeneratedManifestUnderTest",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();

        var result = compilation.Emit(stream);

        Assert.True(
            result.Success,
            "The generated manifest did not compile:" + Environment.NewLine +
            string.Join(Environment.NewLine,
                result.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => "  " + diagnostic)));

        return Assembly.Load(stream.ToArray());
    }

    /// <summary>
    /// A reference to <paramref name="path"/>, or null when it is not a managed assembly. The
    /// output directory holds native runtime files too, and Roslyn refuses the whole compilation
    /// over one of them.
    /// </summary>
    private static MetadataReference? TryReference(string path) {
        try {
            return MetadataReference.CreateFromFile(path);
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException) {
            return null;
        }
    }

    private IStaticContentManifest Build(string? fallBack = null, long embed = 1024 * 1024) {
        var scan = StaticContentScan.Scan(_root, "/", fallBack, embed);
        var source = ManifestEmitter.Emit(scan, "Generated.UnderTest", false);
        var assembly = Compile(source);

        var type = assembly.GetType("Generated.UnderTest.GeneratedStaticContentManifest");

        Assert.NotNull(type);

        return (IStaticContentManifest)Activator.CreateInstance(type!)!;
    }

    [Fact]
    public void AnEmptyManifestCompiles() {
        var manifest = Build();

        Assert.Empty(manifest.Entries);
        Assert.Null(manifest.FallBackRoute);
    }

    /// <summary>
    /// A tree with every shape the emitter has a branch for: embedded, compressed, left on disk,
    /// aliased to a directory, and named the fall back.
    /// </summary>
    [Fact]
    public void AFullManifestCompilesAndCarriesWhatTheScanFound() {
        Write("index.html", "<html>shell</html>");
        Write("app.js", "console.log('hi');");
        Write(Path.Combine("css", "site.css"), new string('a', 3000));
        Write("movie.bin", new string('b', 5000));

        var manifest = Build(fallBack: "index.html", embed: 4096);

        Assert.Equal("/index.html", manifest.FallBackRoute);

        var routes = manifest.Entries.Select(entry => entry.RoutePath).ToList();

        Assert.Contains("/index.html", routes);
        Assert.Contains("/app.js", routes);
        Assert.Contains("/css/site.css", routes);
        Assert.Contains("/movie.bin", routes);

        // The directory alias the scan added for the default document.
        Assert.Contains("/", routes);
    }

    /// <summary>The bytes survive the round trip through generated source unchanged.</summary>
    [Fact]
    public void EmbeddedBytesRoundTrip() {
        var content = new byte[512];

        for (var index = 0; index < content.Length; index++) {
            content[index] = (byte)index;
        }

        File.WriteAllBytes(Path.Combine(_root, "bytes.bin"), content);

        var entry = Build().Entries.Single(candidate => candidate.RoutePath == "/bytes.bin");

        Assert.NotNull(entry.Content);
        Assert.Equal(content, entry.Content);
    }

    /// <summary>
    /// A file over the threshold carries its path rather than its bytes, and the entry says so.
    /// </summary>
    [Fact]
    public void AnEntryLeftOnDiskCarriesItsPath() {
        Write("movie.bin", new string('b', 5000));

        var entry = Build(embed: 1024).Entries.Single(candidate => candidate.RoutePath == "/movie.bin");

        Assert.False(entry.IsEmbedded);
        Assert.Equal("movie.bin", entry.RelativePath);
        Assert.Equal(5000, entry.Length);
    }

    /// <summary>The timestamp survives as a real point in time rather than as ticks nobody reads.</summary>
    [Fact]
    public void TheTimestampRoundTripsAsADate() {
        Write("app.js", "x");

        var entry = Build().Entries.Single(candidate => candidate.RoutePath == "/app.js");

        Assert.Equal(TimeSpan.Zero, entry.LastModified.Offset);
        Assert.InRange(
            entry.LastModified, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
    }

    /// <summary>
    /// A file name carrying characters that mean something in C# still compiles. A quote would
    /// otherwise close the literal and produce a syntax error in a project that did nothing wrong.
    /// </summary>
    [Fact]
    public void AnAwkwardlyNamedFileStillCompiles() {
        try {
            Write("it's \"quoted\" odd.txt", "content");
        }
        catch (Exception exception) when (exception is IOException or ArgumentException) {
            return; // The file system will not take the name; nothing to assert.
        }

        Assert.Single(Build().Entries);
    }
}
