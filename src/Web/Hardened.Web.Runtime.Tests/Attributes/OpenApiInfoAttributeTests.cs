using Hardened.Web.Runtime.Attributes;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Attributes;

public class OpenApiInfoAttributeTests {

    [Fact]
    public void CarriesWhatWasDeclared() {
        var attribute = new OpenApiInfoAttribute("Consignments API", "1.2.0", "The consignments.");

        Assert.Equal("Consignments API", attribute.Title);
        Assert.Equal("1.2.0", attribute.Version);
        Assert.Equal("The consignments.", attribute.Description);
    }

    /// <summary>The defaults an entry point declaring only a title gets.</summary>
    [Fact]
    public void VersionDefaultsAndDescriptionIsAbsent() {
        var attribute = new OpenApiInfoAttribute("Consignments API");

        Assert.Equal("1.0.0", attribute.Version);
        Assert.Null(attribute.Description);
    }
}
