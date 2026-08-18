using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Web.Runtime.Tests.OpenApi;

/// <summary>
/// Two things the module has to get right, both of which look like boilerplate and are not: its
/// properties must survive the generated attribute, and its equality decides how many pages an
/// application can install.
/// </summary>
public class HardenedOpenApiUiTests {

    private static IReadOnlyList<ServiceDescriptor> Register(params HardenedOpenApiUi[] modules) {
        var services = new ServiceCollection();

        foreach (var module in modules) {
            module.ConfigureServices(services);
        }

        return services.ToList();
    }

    private static IOpenApiUiConfiguration Configure(HardenedOpenApiUi module) {
        var services = new ServiceCollection();

        module.ConfigureServices(services);

        var provider = services.BuildServiceProvider()
            .GetServices<IWebExecutionRequestHandlerProvider>()
            .OfType<OpenApiUiProvider>()
            .Single();

        return provider.Configuration;
    }

    #region defaults

    /// <remarks>
    /// DependencyModules generates the module attribute with every property defaulting to
    /// <c>default(T)</c> and copies each onto the module, guarded by a null check only for the ones
    /// declared nullable. A non-nullable <c>string</c> here would be assigned null by
    /// <c>[HardenedOpenApiUi]</c> written with no arguments - so the annotations are load-bearing,
    /// and this is what says so.
    /// </remarks>
    [Fact]
    public void ConfigureServices_UsesTheDefaultsWhenNothingIsSet() {
        var configuration = Configure(new HardenedOpenApiUi());

        Assert.Equal(HardenedOpenApiUi.DefaultPath, configuration.Path);
        Assert.Equal(HardenedOpenApiUi.DefaultTitle, configuration.Title);
        Assert.Equal(HardenedOpenApiUi.DefaultDocumentPath, configuration.DocumentPath);
        Assert.Equal(HardenedOpenApiUi.DefaultScriptUrl, configuration.ScriptUrl);
        Assert.Equal(HardenedOpenApiUi.DefaultScriptIntegrity, configuration.ScriptIntegrity);
    }

    [Fact]
    public void ConfigureServices_CarriesWhatWasSet() {
        var configuration = Configure(new HardenedOpenApiUi {
            Path = "/docs/internal",
            Title = "Internal",
            DocumentPath = "/internal.json",
            ScriptUrl = "/assets/ui.js",
            ScriptIntegrity = ""
        });

        Assert.Equal("/docs/internal", configuration.Path);
        Assert.Equal("Internal", configuration.Title);
        Assert.Equal("/internal.json", configuration.DocumentPath);
        Assert.Equal("/assets/ui.js", configuration.ScriptUrl);
        Assert.Equal("", configuration.ScriptIntegrity);
    }

    [Fact]
    public void ConfigureServices_FallsBackWhenAPropertyIsNulled() {
        var configuration = Configure(new HardenedOpenApiUi {
            Path = null, Title = null, DocumentPath = null, ScriptUrl = null
        });

        Assert.Equal(HardenedOpenApiUi.DefaultPath, configuration.Path);
        Assert.Equal(HardenedOpenApiUi.DefaultTitle, configuration.Title);
        Assert.Equal(HardenedOpenApiUi.DefaultDocumentPath, configuration.DocumentPath);
        Assert.Equal(HardenedOpenApiUi.DefaultScriptUrl, configuration.ScriptUrl);
    }

    /// <summary>
    /// A request path always begins with a slash, so a page configured without one would match
    /// nothing and say nothing about why.
    /// </summary>
    [Fact]
    public void ConfigureServices_GivesAPathWithoutALeadingSlashOne() {
        Assert.Equal("/docs/internal", Configure(new HardenedOpenApiUi { Path = "docs/internal" }).Path);
    }

    #endregion

    #region equality, which is what decides how many pages install

    /// <summary>
    /// DependencyModules loads a module once per distinct value of its equality, so this is the whole
    /// mechanism behind installing a page per published specification.
    /// </summary>
    [Fact]
    public void Equals_TellsTwoPathsApart() {
        Assert.NotEqual(
            new HardenedOpenApiUi { Path = "/docs" },
            new HardenedOpenApiUi { Path = "/docs/internal" });
    }

    /// <summary>
    /// And treats one path as one page, however differently the two are configured. A second page at
    /// a path already taken could not be reached in any case - providers are consulted in reverse
    /// registration order, so it would only shadow the first.
    /// </summary>
    [Fact]
    public void Equals_TreatsOnePathAsOnePageWhateverElseDiffers() {
        Assert.Equal(
            new HardenedOpenApiUi { Path = "/docs", Title = "One", DocumentPath = "/a.json" },
            new HardenedOpenApiUi { Path = "/docs", Title = "Two", DocumentPath = "/b.json" });
    }

    [Fact]
    public void Equals_HoldsForTheDefaultPathHoweverItIsSpelled() {
        Assert.Equal(new HardenedOpenApiUi(), new HardenedOpenApiUi { Path = "docs" });
        Assert.Equal(new HardenedOpenApiUi(), new HardenedOpenApiUi { Path = null });
    }

    [Fact]
    public void GetHashCode_AgreesWithEquals() {
        Assert.Equal(
            new HardenedOpenApiUi { Path = "/docs", Title = "One" }.GetHashCode(),
            new HardenedOpenApiUi { Path = "docs", Title = "Two" }.GetHashCode());

        Assert.NotEqual(
            new HardenedOpenApiUi { Path = "/docs" }.GetHashCode(),
            new HardenedOpenApiUi { Path = "/other" }.GetHashCode());
    }

    #endregion

    /// <summary>
    /// Each page holds its own configuration rather than resolving one. Several may be installed, so
    /// a single registered <c>IOpenApiUiConfiguration</c> would be whichever module ran last and
    /// every page would render it.
    /// </summary>
    [Fact]
    public void ConfigureServices_RegistersNoSharedConfiguration() {
        var registered = Register(
            new HardenedOpenApiUi(),
            new HardenedOpenApiUi { Path = "/docs/internal", Title = "Internal" });

        Assert.DoesNotContain(
            registered, service => service.ServiceType == typeof(IOpenApiUiConfiguration));

        Assert.Equal(
            2,
            registered.Count(
                service => service.ServiceType == typeof(IWebExecutionRequestHandlerProvider)));
    }

    /// <summary>
    /// The controller is shared, and registered once however many pages install.
    /// </summary>
    [Fact]
    public void ConfigureServices_RegistersTheControllerOnce() {
        var registered = Register(
            new HardenedOpenApiUi(),
            new HardenedOpenApiUi { Path = "/docs/internal" });

        Assert.Single(registered, service => service.ServiceType == typeof(OpenApiUiController));
    }

    /// <summary>
    /// The pinned URL and the hash have to move together: a hash is of exact bytes, so a URL bumped
    /// without its hash produces a page whose script never runs.
    /// </summary>
    [Fact]
    public void DefaultScriptUrl_IsVersionPinnedSoItsIntegrityHashCanBe() {
        Assert.Contains("@scalar/api-reference@", HardenedOpenApiUi.DefaultScriptUrl);
        Assert.DoesNotContain("@latest", HardenedOpenApiUi.DefaultScriptUrl);
        Assert.StartsWith("sha384-", HardenedOpenApiUi.DefaultScriptIntegrity);
    }
}
