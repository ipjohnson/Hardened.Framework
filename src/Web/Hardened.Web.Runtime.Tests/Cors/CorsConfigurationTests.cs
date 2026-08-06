using Hardened.Web.Runtime.Cors;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Cors;

public class CorsConfigurationTests {

    [Fact]
    public void AllowedOriginIsMatched() {
        var config = new CorsConfiguration();
        config.AllowOrigin("https://app.example.com");

        Assert.True(config.IsOriginAllowed("https://app.example.com"));
    }

    [Fact]
    public void UnknownOriginIsNotMatched() {
        var config = new CorsConfiguration();
        config.AllowOrigin("https://app.example.com");

        Assert.False(config.IsOriginAllowed("https://evil.example.com"));
    }

    [Fact]
    public void MatchingIsCaseInsensitive() {
        var config = new CorsConfiguration();
        config.AllowOrigin("https://App.Example.com");

        Assert.True(config.IsOriginAllowed("https://app.example.COM"));
    }

    /// <summary>
    /// Trailing slashes are normalised on both sides, so a configured
    /// "https://app.example.com/" matches a browser-sent "https://app.example.com".
    /// </summary>
    [Theory]
    [InlineData("https://app.example.com/", "https://app.example.com")]
    [InlineData("https://app.example.com", "https://app.example.com/")]
    [InlineData("https://app.example.com/", "https://app.example.com/")]
    public void TrailingSlashesAreNormalisedOnBothSides(string configured, string requested) {
        var config = new CorsConfiguration();
        config.AllowOrigin(configured);

        Assert.True(config.IsOriginAllowed(requested));
    }

    [Fact]
    public void NothingIsAllowedByDefault() {
        Assert.False(new CorsConfiguration().IsOriginAllowed("https://app.example.com"));
        Assert.Empty(new CorsConfiguration().AllowedOrigins);
    }

    [Fact]
    public void DefaultsAreSensible() {
        var config = new CorsConfiguration();

        Assert.Equal(86400, config.MaxAgeSec);
        Assert.Contains("GET", config.AllowedMethods);
        Assert.Contains("OPTIONS", config.AllowedMethods);
        Assert.Contains("Authorization", config.AllowedHeaders);
        Assert.Equal(CorsConfiguration.DefaultEnvironmentVariable, config.EnvironmentVariable);
    }

    [Fact]
    public void LoadFromEnvironmentReadsCommaSeparatedOrigins() {
        var variable = "CORS_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable,
            "https://a.example.com, https://b.example.com ,https://c.example.com");

        try {
            var config = new CorsConfiguration { EnvironmentVariable = variable };
            config.LoadFromEnvironment();

            Assert.True(config.IsOriginAllowed("https://a.example.com"));
            Assert.True(config.IsOriginAllowed("https://b.example.com"));
            Assert.True(config.IsOriginAllowed("https://c.example.com"));
            Assert.Equal(3, config.AllowedOrigins.Count);
        }
        finally {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void LoadFromEnvironmentIsANoOpWhenUnset() {
        var config = new CorsConfiguration {
            EnvironmentVariable = "CORS_TEST_UNSET_" + Guid.NewGuid().ToString("N")
        };

        config.LoadFromEnvironment();

        Assert.Empty(config.AllowedOrigins);
    }

    [Fact]
    public void LoadFromEnvironmentIsANoOpWhenBlank() {
        var variable = "CORS_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "   ");

        try {
            var config = new CorsConfiguration { EnvironmentVariable = variable };
            config.LoadFromEnvironment();

            Assert.Empty(config.AllowedOrigins);
        }
        finally {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void LoadFromEnvironmentSkipsEmptyEntries() {
        var variable = "CORS_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "https://a.example.com,,https://b.example.com,");

        try {
            var config = new CorsConfiguration { EnvironmentVariable = variable };
            config.LoadFromEnvironment();

            Assert.Equal(2, config.AllowedOrigins.Count);
        }
        finally {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }
}
