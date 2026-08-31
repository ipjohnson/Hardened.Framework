using Hardened.Requests.Abstract.Authorization;
using Xunit;

namespace Hardened.Requests.Abstract.Tests.Authorization;

/// <summary>
/// The scheme-shape attributes carry what was declared. The generator reads them as symbols;
/// these pin the runtime surface an application or a tool reading the metadata sees.
/// </summary>
public class AuthenticationSchemeAttributeTests {

    [Fact]
    public void HttpSchemeCarriesItsShape() {
        var attribute = new HttpAuthenticationSchemeAttribute("bearer") {
            BearerFormat = "JWT",
            Description = "The bearer."
        };

        Assert.Equal("bearer", attribute.Scheme);
        Assert.Equal("JWT", attribute.BearerFormat);
        Assert.Equal("The bearer.", attribute.Description);
    }

    [Fact]
    public void ApiKeySchemeCarriesItsShape() {
        var attribute = new ApiKeyAuthenticationSchemeAttribute("X-Api-Key", ApiKeyLocation.Header) {
            Description = "The key."
        };

        Assert.Equal("X-Api-Key", attribute.Name);
        Assert.Equal(ApiKeyLocation.Header, attribute.Location);
        Assert.Equal("The key.", attribute.Description);
    }

    [Fact]
    public void OAuth2SchemeCarriesItsShape() {
        var attribute = new OAuth2AuthenticationSchemeAttribute(OAuth2Flow.ClientCredentials) {
            TokenUrl = "https://id.example/token",
            AuthorizationUrl = "https://id.example/authorize",
            RefreshUrl = "https://id.example/refresh",
            Description = "The flow."
        };

        Assert.Equal(OAuth2Flow.ClientCredentials, attribute.Flow);
        Assert.Equal("https://id.example/token", attribute.TokenUrl);
        Assert.Equal("https://id.example/authorize", attribute.AuthorizationUrl);
        Assert.Equal("https://id.example/refresh", attribute.RefreshUrl);
        Assert.Equal("The flow.", attribute.Description);
    }
}
