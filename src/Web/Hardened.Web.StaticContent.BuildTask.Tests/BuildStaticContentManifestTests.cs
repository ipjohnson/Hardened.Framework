using System.Collections;
using Hardened.Web.StaticContent.BuildTask;
using Microsoft.Build.Framework;

namespace Hardened.Web.StaticContent.BuildTask.Tests;

/// <summary>
/// The task itself: what it writes, what it reports, and when it refuses.
///
/// <para>
/// Thin by design - the scan and the emitter are where the work is - but the thin part is what
/// decides whether a build fails, and a diagnostic that is logged as a message rather than an error
/// is a build that succeeds while serving a secret.
/// </para>
/// </summary>
public class BuildStaticContentManifestTests : IDisposable {

    private readonly string _root;
    private readonly string _output;

    public BuildStaticContentManifestTests() {
        _root = Path.Combine(Path.GetTempPath(), "hardened-task-" + Guid.NewGuid().ToString("N"));
        _output = Path.Combine(_root, "obj", "manifest.g.cs");

        Directory.CreateDirectory(Path.Combine(_root, "content"));
    }

    public void Dispose() {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }

        GC.SuppressFinalize(this);
    }

    private void Write(string relative, string content) {
        var path = Path.Combine(_root, "content", relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Records what the task logged, which is the half of its behaviour MSBuild sees.</summary>
    private sealed class RecordingEngine : IBuildEngine {
        public List<BuildErrorEventArgs> Errors { get; } = new();

        public List<BuildWarningEventArgs> Warnings { get; } = new();

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e) { }

        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(
            string projectFileName, string[] targetNames, IDictionary globalProperties,
            IDictionary targetOutputs) => true;

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "test.csproj";
    }

    private (bool Result, RecordingEngine Engine, BuildStaticContentManifest Task) Run(
        string? fallBack = null, long embed = 1024 * 1024, string? contentDirectory = null) {
        var engine = new RecordingEngine();

        var task = new BuildStaticContentManifest {
            BuildEngine = engine,
            ContentDirectory = contentDirectory ?? Path.Combine(_root, "content"),
            OutputFile = _output,
            Namespace = "Contoso.Orders",
            FallBackFile = fallBack,
            EmbedThresholdBytes = embed
        };

        return (task.Execute(), engine, task);
    }

    #region what it writes

    [Fact]
    public void ASuccessfulRunWritesTheManifest() {
        Write("app.js", "console.log('hi');");

        var (result, engine, task) = Run();

        Assert.True(result);
        Assert.Empty(engine.Errors);
        Assert.True(File.Exists(_output));
        Assert.Equal(_output, task.GeneratedSource);

        var source = File.ReadAllText(_output);

        Assert.Contains("namespace Contoso.Orders;", source);
        Assert.Contains("\"/app.js\"", source);
    }

    /// <summary>The output directory is created rather than assumed.</summary>
    [Fact]
    public void TheOutputDirectoryIsCreated() {
        Write("app.js", "x");

        Assert.False(Directory.Exists(Path.GetDirectoryName(_output)));
        Assert.True(Run().Result);
        Assert.True(File.Exists(_output));
    }

    /// <summary>
    /// An unchanged tree leaves the file alone. It is an input to the compilation, so rewriting it
    /// with identical bytes moves its timestamp and makes the next build a full rebuild.
    /// </summary>
    [Fact]
    public void AnUnchangedTreeDoesNotTouchTheFile() {
        Write("app.js", "console.log('hi');");

        Assert.True(Run().Result);

        var firstWrite = File.GetLastWriteTimeUtc(_output);

        // Coarse file-time granularity would hide a rewrite, so the check is on the timestamp
        // having moved at all rather than on how far.
        File.SetLastWriteTimeUtc(_output, firstWrite.AddDays(-1));

        var marker = File.GetLastWriteTimeUtc(_output);

        Assert.True(Run().Result);
        Assert.Equal(marker, File.GetLastWriteTimeUtc(_output));
    }

    /// <summary>And a changed tree does rewrite it.</summary>
    [Fact]
    public void AChangedTreeRewritesTheFile() {
        Write("app.js", "console.log('hi');");

        Assert.True(Run().Result);

        var before = File.ReadAllText(_output);

        Write("extra.css", "body{}");

        Assert.True(Run().Result);
        Assert.NotEqual(before, File.ReadAllText(_output));
    }

    #endregion

    #region what it refuses

    /// <summary>
    /// A missing directory fails the build rather than emitting a manifest with nothing in it,
    /// which would read as an application that serves no files and is meant to.
    /// </summary>
    [Fact]
    public void AMissingContentDirectoryFailsTheBuild() {
        var (result, engine, _) = Run(contentDirectory: Path.Combine(_root, "no-such-directory"));

        Assert.False(result);
        Assert.Equal("HSTATIC001", Assert.Single(engine.Errors).Code);
        Assert.False(File.Exists(_output));
    }

    /// <summary>
    /// A fall back file that is not there fails the build. At run time it was an exception raised
    /// on every unknown path, forever - so a typo turned every 404 into a 500 in production.
    /// </summary>
    [Fact]
    public void AMissingFallBackFileFailsTheBuild() {
        Write("app.js", "console.log('hi');");

        var (result, engine, _) = Run(fallBack: "index.html");

        Assert.False(result);
        Assert.Equal("HSTATIC005", Assert.Single(engine.Errors).Code);
    }

    /// <summary>Nothing is written when the build is going to fail.</summary>
    [Fact]
    public void AFailedRunWritesNothing() {
        Write("app.js", "console.log('hi');");

        Assert.False(Run(fallBack: "missing.html").Result);
        Assert.False(File.Exists(_output));
    }

    /// <summary>
    /// A directory the scan cannot even look at fails the build with a code, rather than escaping
    /// as an unhandled exception with an MSBuild stack trace in it.
    /// </summary>
    [Fact]
    public void ADirectoryThatCannotBeReadFailsWithACode() {
        var (result, engine, _) = Run(contentDirectory: "");

        Assert.False(result);
        Assert.Equal("HSTATIC000", Assert.Single(engine.Errors).Code);
    }

    #endregion

    #region what it reports

    /// <summary>
    /// A secret-looking file is a warning and not an error, so the build continues and the file is
    /// still served. It is a prompt to look, not a policy.
    /// </summary>
    [Fact]
    public void ASecretLookingFileIsAWarningAndTheBuildContinues() {
        Write("app.js", "x");
        Write(".env", "SECRET_KEY=abc");

        var (result, engine, _) = Run();

        Assert.True(result);
        Assert.Empty(engine.Errors);
        Assert.Equal("HSTATIC003", Assert.Single(engine.Warnings).Code);
        Assert.True(File.Exists(_output));
    }

    [Fact]
    public void AnEmptyDirectoryIsAWarningAndTheBuildContinues() {
        var (result, engine, _) = Run();

        Assert.True(result);
        Assert.Equal("HSTATIC004", Assert.Single(engine.Warnings).Code);
        Assert.True(File.Exists(_output));
    }

    /// <summary>
    /// The threshold reaches the scan, so a project can decide what travels in the assembly and
    /// what stays beside it.
    /// </summary>
    [Fact]
    public void TheEmbedThresholdIsHonoured() {
        Write("big.bin", new string('a', 5000));

        Assert.True(Run(embed: 1024).Result);
        Assert.DoesNotContain("private static readonly byte[]", File.ReadAllText(_output));

        Assert.True(Run(embed: 1024 * 1024).Result);
        Assert.Contains("private static readonly byte[]", File.ReadAllText(_output));
    }

    #endregion
}
