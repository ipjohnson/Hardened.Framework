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
        Assert.Contains("GET", config.FallbackMethods);
        Assert.Contains("OPTIONS", config.FallbackMethods);
        Assert.Contains("Authorization", config.AllowedHeaders);
        Assert.Equal(CorsConfiguration.DefaultEnvironmentVariable, config.EnvironmentVariable);
    }

    /// <summary>
    /// Nothing configured is a configuration in its own right - it refuses every cross-origin
    /// request - and is distinguishable from the misspelled environment variable that produces it
    /// by accident.
    /// </summary>
    [Fact]
    public void IsConfigured_IsFalseUntilSomethingIsAllowed() {
        Assert.False(new CorsConfiguration().IsConfigured);

        var withOrigin = new CorsConfiguration();
        withOrigin.AllowOrigin("https://app.example.com");
        Assert.True(withOrigin.IsConfigured);

        var withSuffix = new CorsConfiguration();
        withSuffix.AllowOriginSuffix("example.com");
        Assert.True(withSuffix.IsConfigured);

        Assert.True(new CorsConfiguration { AllowAnyOrigin = true }.IsConfigured);
    }

    /// <summary>
    /// A suffix rule admits subdomains and nothing that merely ends the same way -
    /// <c>example.com</c> must not admit <c>notexample.com</c>.
    /// </summary>
    [Theory]
    [InlineData("https://app.example.com", true)]
    [InlineData("https://deep.app.example.com", true)]
    [InlineData("https://notexample.com", false)]
    [InlineData("https://example.com.evil.net", false)]
    [InlineData("https://example.com", false)]
    public void AllowOriginSuffix_AdmitsSubdomainsOnly(string origin, bool expected) {
        var config = new CorsConfiguration();

        config.AllowOriginSuffix("example.com");

        Assert.Equal(expected, config.IsOriginAllowed(origin));
    }

    /// <summary>A port does not defeat a suffix rule.</summary>
    [Fact]
    public void AllowOriginSuffix_IgnoresThePort() {
        var config = new CorsConfiguration();

        config.AllowOriginSuffix("example.com");

        Assert.True(config.IsOriginAllowed("https://app.example.com:8443"));
    }

    /// <summary>Any origin admits everything, which is the point and why it is opt-in.</summary>
    [Fact]
    public void AllowAnyOrigin_AdmitsAnything() {
        Assert.True(
            new CorsConfiguration { AllowAnyOrigin = true }.IsOriginAllowed("https://evil.example"));
    }

    /// <summary>
    /// The environment variable understands the two wildcard spellings, so a deployment can
    /// configure a suffix without code.
    /// </summary>
    [Fact]
    public void LoadFromEnvironment_UnderstandsWildcards() {
        var variable = "CORS_TEST_" + Guid.NewGuid().ToString("N");

        Environment.SetEnvironmentVariable(variable, "*.example.com, https://other.test");

        try {
            var config = new CorsConfiguration { EnvironmentVariable = variable };

            config.LoadFromEnvironment();

            Assert.True(config.IsOriginAllowed("https://app.example.com"));
            Assert.True(config.IsOriginAllowed("https://other.test"));
            Assert.False(config.IsOriginAllowed("https://nope.test"));
        }
        finally {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void LoadFromEnvironment_TreatsAStarAsAnyOrigin() {
        var variable = "CORS_TEST_" + Guid.NewGuid().ToString("N");

        Environment.SetEnvironmentVariable(variable, "*");

        try {
            var config = new CorsConfiguration { EnvironmentVariable = variable };

            config.LoadFromEnvironment();

            Assert.True(config.AllowAnyOrigin);
        }
        finally {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>
    /// The allowed-header set is what a preflight is checked against, and it is case-insensitive
    /// because header names are.
    /// </summary>
    [Fact]
    public void AreHeadersAllowed_IsCaseInsensitiveAndRequiresEveryHeader() {
        var config = new CorsConfiguration();

        Assert.True(config.AreHeadersAllowed(new[] { "content-type", "AUTHORIZATION" }));
        Assert.False(config.AreHeadersAllowed(new[] { "Content-Type", "X-Nope" }));

        config.AllowHeader("X-Nope");

        Assert.True(config.AreHeadersAllowed(new[] { "Content-Type", "X-Nope" }));
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
