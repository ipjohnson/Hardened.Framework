using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Web.Runtime.Tests.OpenApi;

/// <summary>
/// The module's properties are nullable so that an unset one keeps its default, and that is the
/// thing most likely to be undone by someone tidying the annotations - so it is asserted rather
/// than commented.
/// </summary>
/// <remarks>
/// DependencyModules generates the module attribute with every property defaulting to
/// <c>default(T)</c> and copies each onto the module, guarded by a null check only for the ones
/// declared nullable. A non-nullable <c>string</c> here would therefore be assigned null by
/// <c>[HardenedOpenApiUi]</c> written with no arguments, and the page would render with an empty
/// title, no document URL and no script.
/// </remarks>
public class HardenedOpenApiUiTests {

    private static IOpenApiUiConfiguration Configure(HardenedOpenApiUi module) {
        var services = new ServiceCollection();

        module.ConfigureServices(services);

        return services.BuildServiceProvider().GetRequiredService<IOpenApiUiConfiguration>();
    }

    [Fact]
    public void ConfigureServices_UsesTheDefaultsWhenNothingIsSet() {
        var configuration = Configure(new HardenedOpenApiUi());

        Assert.Equal(HardenedOpenApiUi.DefaultTitle, configuration.Title);
        Assert.Equal(HardenedOpenApiUi.DefaultDocumentPath, configuration.DocumentPath);
        Assert.Equal(HardenedOpenApiUi.DefaultScriptUrl, configuration.ScriptUrl);
        Assert.Equal(HardenedOpenApiUi.DefaultScriptIntegrity, configuration.ScriptIntegrity);
    }

    [Fact]
    public void ConfigureServices_CarriesWhatWasSet() {
        var configuration = Configure(new HardenedOpenApiUi {
            Title = "Contoso Orders",
            DocumentPath = "/spec.yaml",
            ScriptUrl = "/assets/ui.js",
            ScriptIntegrity = ""
        });

        Assert.Equal("Contoso Orders", configuration.Title);
        Assert.Equal("/spec.yaml", configuration.DocumentPath);
        Assert.Equal("/assets/ui.js", configuration.ScriptUrl);
        Assert.Equal("", configuration.ScriptIntegrity);
    }

    /// <summary>
    /// Null is not a value anyone can mean, because it is what "not set" looks like - so a module
    /// constructed directly with nulls still produces a usable page rather than an empty one.
    /// </summary>
    [Fact]
    public void ConfigureServices_FallsBackWhenAPropertyIsNulled() {
        var configuration = Configure(new HardenedOpenApiUi {
            Title = null, DocumentPath = null, ScriptUrl = null
        });

        Assert.Equal(HardenedOpenApiUi.DefaultTitle, configuration.Title);
        Assert.Equal(HardenedOpenApiUi.DefaultDocumentPath, configuration.DocumentPath);
        Assert.Equal(HardenedOpenApiUi.DefaultScriptUrl, configuration.ScriptUrl);
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
