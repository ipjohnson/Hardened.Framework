using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Hardened.Web.StaticContent.Tests;

/// <summary>
/// A directory read at run time, served under <see cref="IStaticContentConfiguration.RoutePrefix"/>.
/// </summary>
public class StaticContentRoutePrefixTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _staticRoot;

    public StaticContentRoutePrefixTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "hardened-prefix-" + Guid.NewGuid().ToString("N")
        );
        _staticRoot = Path.Combine(_tempRoot, "wwwroot");

        Directory.CreateDirectory(Path.Combine(_staticRoot, "nested"));

        File.WriteAllText(Path.Combine(_staticRoot, "small.json"), "{}");
        File.WriteAllText(Path.Combine(_staticRoot, "index.html"), "<html>shell</html>");
        File.WriteAllText(Path.Combine(_staticRoot, "nested", "page.html"), "<html>page</html>");

        // Beside the directory rather than in it, so a traversal that escaped would find it.
        File.WriteAllText(Path.Combine(_tempRoot, "secret.txt"), "SECRET");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, true);
        }
        catch
        { /* best effort */
        }

        GC.SuppressFinalize(this);
    }

    #region harness

    private FileSystemContentSource Source(
        string? routePrefix,
        string? fallBackFile = null,
        bool serveHiddenFiles = false
    )
    {
        var configuration = Substitute.For<IStaticContentConfiguration>();

        configuration.Path.Returns(_staticRoot);
        configuration.RoutePrefix.Returns(routePrefix!);
        configuration.CacheContent.Returns(true);
        configuration.FallBackFile.Returns(fallBackFile);
        configuration.ServeHiddenFiles.Returns(serveHiddenFiles);

        var mimeHelper = Substitute.For<IFileExtToMimeTypeHelper>();

        mimeHelper.GetMimeTypeInfo(Arg.Any<string>()).Returns(("text/plain", false));

        return new FileSystemContentSource(
            Options.Create(configuration),
            mimeHelper,
            new GZipStaticContentCompressor(new MemoryStreamPool()),
            new ETagProvider(new TestHashPool()),
            NullLogger<FileSystemContentSource>.Instance
        );
    }

    private string OnDisk(string relative) =>
        Path.Combine(_staticRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// The configuration an application composes from the module and
    /// <c>ConfigureStaticContent</c>, read the way the source reads it.
    /// </summary>
    private static IStaticContentConfiguration Configured(
        HardenedStaticContent module,
        Action<StaticContentConfiguration>? configure = null
    )
    {
        var services = new ServiceCollection();

        module.ConfigureServices(services);

        if (configure != null)
        {
            services.ConfigureStaticContent(configure);
        }

        services.AddSingleton<IHardenedEnvironment>(new EnvironmentImpl());
        services.AddSingleton<IConfigurationManager, ConfigurationManager>();

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<IStaticContentConfiguration>>().Value;
    }

    #endregion

    #region the directory source

    [Theory]
    [InlineData("/static/small.json", "small.json")]
    [InlineData("/static/nested/page.html", "nested/page.html")]
    public void APathUnderThePrefixIsServedFromTheDirectory(string path, string file)
    {
        Assert.Equal(OnDisk(file), Source("/static").Locate(path)?.FilePath);
    }

    /// <summary>
    /// Declined, so something else answers. That includes a path that only starts with the same
    /// letters, and one that differs in case.
    /// </summary>
    [Theory]
    [InlineData("/small.json")]
    [InlineData("/other/small.json")]
    [InlineData("/staticfiles/small.json")]
    [InlineData("/Static/small.json")]
    [InlineData("/stat")]
    [InlineData("/")]
    public void APathOutsideThePrefixIsDeclined(string path)
    {
        Assert.Null(Source("/static").Locate(path));
    }

    /// <summary>Both spellings serve the default document, as the manifest's aliases do.</summary>
    [Theory]
    [InlineData("/static")]
    [InlineData("/static/")]
    public void ThePrefixItselfServesTheDefaultDocument(string path)
    {
        Assert.Equal(OnDisk("index.html"), Source("/static").Locate(path)?.FilePath);
    }

    /// <summary>The slashes are settled by the rules the build task applies to an item's prefix.</summary>
    [Theory]
    [InlineData("static")]
    [InlineData("/static")]
    [InlineData("static/")]
    [InlineData("/static/")]
    [InlineData(" /static ")]
    public void ThePrefixIsReadWithOrWithoutItsSlashes(string routePrefix)
    {
        var source = Source(routePrefix);

        Assert.Equal(OnDisk("small.json"), source.Locate("/static/small.json")?.FilePath);
        Assert.Null(source.Locate("/small.json"));
    }

    [Fact]
    public void APrefixOfSeveralSegmentsIsRemovedWhole()
    {
        var source = Source("/assets/v1");

        Assert.Equal(OnDisk("small.json"), source.Locate("/assets/v1/small.json")?.FilePath);
        Assert.Null(source.Locate("/assets/small.json"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    public void ARootPrefixServesEveryPath(string? routePrefix)
    {
        Assert.Equal(OnDisk("small.json"), Source(routePrefix).Locate("/small.json")?.FilePath);
    }

    /// <summary>
    /// The fall back file answers an unknown path under the prefix and nothing outside it. A
    /// single-page application under <c>/app</c> would otherwise answer <c>/api/typo</c> with its
    /// shell.
    /// </summary>
    [Fact]
    public void TheFallBackFileAnswersOnlyUnderThePrefix()
    {
        var source = Source("/app", fallBackFile: "/index.html");

        var fellBack = source.Locate("/app/orders/42");

        Assert.Equal(OnDisk("index.html"), fellBack?.FilePath);
        Assert.True(fellBack?.IsFallback);
        Assert.Null(source.Locate("/api/typo"));
    }

    /// <summary>
    /// What is left after the prefix is contained like any other path. Hidden files are served
    /// here, so the containment check is what refuses these. <c>..</c> is otherwise refused as a
    /// hidden segment first.
    /// </summary>
    [Theory]
    [InlineData("/static/../secret.txt")]
    [InlineData("/static/nested/../../secret.txt")]
    public void ATraversalAfterThePrefixIsRefused(string path)
    {
        Assert.Null(Source("/static", serveHiddenFiles: true).Locate(path));
    }

    [Fact]
    public void AHiddenFileUnderThePrefixIsNotServed()
    {
        File.WriteAllText(OnDisk(".env"), "SECRET=1");

        Assert.Null(Source("/static").Locate("/static/.env"));
    }

    #endregion

    #region configuration

    [Fact]
    public void TheAttributeSetsThePrefix()
    {
        var module = new HardenedStaticContentAttribute { RoutePrefix = "/static" }.GetModule();

        Assert.Equal("/static", Configured((HardenedStaticContent)module).RoutePrefix);
    }

    [Fact]
    public void WithoutOneTheFilesAreServedAtTheRoot()
    {
        var module = new HardenedStaticContentAttribute().GetModule();

        Assert.Equal("/", Configured((HardenedStaticContent)module).RoutePrefix);
    }

    /// <summary>
    /// Where a prefix goes when the directory comes from configuration, and it runs after the
    /// attribute.
    /// </summary>
    [Fact]
    public void ConfigureStaticContentSetsThePrefix()
    {
        Assert.Equal(
            "/files",
            Configured(
                new HardenedStaticContent { RoutePrefix = "/static" },
                content => content.RoutePrefix = "/files"
            ).RoutePrefix
        );
    }

    #endregion

    #region the manifest

    /// <summary>
    /// The build fixed a manifest's routes, so a run-time prefix is ignored, and a warning says so.
    /// </summary>
    [Theory]
    [InlineData("/static", true)]
    [InlineData("/", false)]
    [InlineData("", false)]
    public void AManifestReportsThatItIgnoresARunTimePrefix(string routePrefix, bool reported)
    {
        var warnings = new List<string>();
        var configuration = Substitute.For<IStaticContentConfiguration>();
        var manifest = Substitute.For<IStaticContentManifest>();

        configuration.Path.Returns(_staticRoot);
        configuration.RoutePrefix.Returns(routePrefix);
        manifest.Entries.Returns(Array.Empty<StaticContentManifestEntry>());

        _ = new ManifestContentSource(
            manifest,
            Options.Create(configuration),
            Substitute.For<IFileExtToMimeTypeHelper>(),
            new CollectingLogger<ManifestContentSource>(warnings)
        );

        Assert.Equal(reported, warnings.Any(message => message.Contains("is ignored")));
    }

    #endregion
}
