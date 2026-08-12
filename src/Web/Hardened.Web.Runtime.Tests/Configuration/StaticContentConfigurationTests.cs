using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.CacheControl;
using Hardened.Web.Runtime.Configuration;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Configuration;

/// <summary>
/// The defaults an application gets when it registers the web module and configures nothing.
///
/// <para>
/// These are the values <c>StaticContentHandler</c> is constructed with in every application that
/// does not override them, so each one is a shipped behaviour: where files are served from,
/// whether they are compressed, whether a conditional request can be answered, and whether an
/// unknown path falls back to an application shell.
/// </para>
/// </summary>
public class StaticContentConfigurationTests {

    [Fact]
    public void FilesAreServedFromWwwrootByDefault() {
        Assert.Equal("wwwroot", new StaticContentConfiguration().Path);
    }

    [Fact]
    public void TextContentIsCompressedByDefault() {
        Assert.True(new StaticContentConfiguration().CompressTextContent);
    }

    [Fact]
    public void ETagsAreEnabledByDefault() {
        Assert.True(new StaticContentConfiguration().EnableETag);
    }

    /// <summary>
    /// There is no fallback file by default, so an unknown path is a 404 rather than the
    /// application shell. Single-page applications opt in.
    /// </summary>
    [Fact]
    public void ThereIsNoFallbackFileByDefault() {
        Assert.Null(new StaticContentConfiguration().FallBackFile);
    }

    /// <summary>
    /// A max age of zero, not null — so the handler emits <c>Cache-Control: max-age=0</c> by
    /// default rather than no header at all, and content is revalidated rather than cached
    /// indefinitely by a heuristic.
    /// </summary>
    [Fact]
    public void TheDefaultMaxAgeIsZeroRatherThanAbsent() {
        Assert.Equal(0, new StaticContentConfiguration().CacheMaxAge);
    }

    [Fact]
    public void ContentIsNotImmutableByDefault() {
        Assert.False(new StaticContentConfiguration().Immutable);
    }

    [Fact]
    public void TheDefaultCacheControlTypeIsAPublicMaxAge() {
        Assert.Equal(
            CacheControlEnum.MaxAge | CacheControlEnum.Public,
            new StaticContentConfiguration().CacheControlType);
    }

    [Fact]
    public void ThereIsNoPrepareResponseCallbackByDefault() {
        Assert.Null(new StaticContentConfiguration().OnPrepareResponse);
    }

    /// <summary>
    /// Every value is settable through the interface the handler reads it through — the handler
    /// takes <c>IOptions&lt;IStaticContentConfiguration&gt;</c>, so a property the concrete class
    /// sets but the interface does not expose is unreachable.
    /// </summary>
    [Fact]
    public void EveryConfiguredValueIsVisibleThroughTheInterface() {
        Action<IExecutionContext> callback = _ => { };

        IStaticContentConfiguration configuration = new StaticContentConfiguration {
            Path = "public",
            CacheControlType = CacheControlEnum.NoStore,
            CacheMaxAge = 3600,
            Immutable = true,
            EnableETag = false,
            FallBackFile = "/index.html",
            CompressTextContent = false,
            OnPrepareResponse = callback
        };

        Assert.Equal("public", configuration.Path);
        Assert.Equal(CacheControlEnum.NoStore, configuration.CacheControlType);
        Assert.Equal(3600, configuration.CacheMaxAge);
        Assert.True(configuration.Immutable);
        Assert.False(configuration.EnableETag);
        Assert.Equal("/index.html", configuration.FallBackFile);
        Assert.False(configuration.CompressTextContent);
        Assert.Same(callback, configuration.OnPrepareResponse);
    }
}
