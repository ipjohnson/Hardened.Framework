using DependencyModules.Runtime;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Jwt.Testing;
using Hardened.Requests.Runtime.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Jwt.Tests;

/// <summary>
/// What stops the application before its first request, and what only warns.
/// </summary>
public class JwtBearerStartupServiceTests
{
    private sealed class RecordingLogger : ILoggerProvider, ILogger
    {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }

        public void Dispose() { }
    }

    private static IServiceProvider Provider(
        JwtBearerConfiguration configuration,
        IJwtSigningKeySource? keys = null,
        ILoggerProvider? logger = null
    )
    {
        var services = new ServiceCollection();

        services.AddSingleton(Options.Create<IJwtBearerConfiguration>(configuration));
        services.AddSingleton(keys ?? new JwksSigningKeySource(configuration));

        if (logger != null)
        {
            services.AddLogging(logging => logging.AddProvider(logger));
        }

        return services.BuildServiceProvider();
    }

    private static JwtBearerConfiguration Configured() =>
        new() { Issuer = TestJwtIssuer.DefaultIssuer, Audience = TestJwtIssuer.DefaultAudience };

    [Theory]
    [InlineData("JWT_ISSUER")]
    [InlineData("JWT_AUDIENCE")]
    public async Task AMissingIssuerOrAudienceStopsTheApplication(string variable)
    {
        var configuration = Configured();

        if (variable == "JWT_ISSUER")
        {
            configuration.Issuer = "";
        }
        else
        {
            configuration.Audience = "";
        }

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new JwtBearerStartupService().Startup(Provider(configuration))
        );

        Assert.Contains(variable, failure.Message);
    }

    [Fact]
    public async Task NowhereToReadTheKeysFromStopsTheApplication()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new JwtBearerStartupService().Startup(Provider(Configured()))
        );

        Assert.Contains("JWT_AUTHORITY", failure.Message);
        Assert.Contains("JWT_JWKS_URL", failure.Message);
    }

    [Fact]
    public async Task APlainHttpKeyAddressStopsTheApplication()
    {
        var configuration = Configured();

        configuration.JwksUrl = "http://issuer.test/keys";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new JwtBearerStartupService().Startup(Provider(configuration))
        );
    }

    /// <summary>
    /// A key source of the application's own needs no key address.
    /// </summary>
    [Fact]
    public async Task AnotherKeySourceNeedsNoKeyAddress()
    {
        var configuration = Configured();

        Assert.True(
            await new JwtBearerStartupService().Startup(
                Provider(
                    configuration,
                    new TestJwtIssuer(Options.Create<IJwtBearerConfiguration>(configuration))
                )
            )
        );
    }

    /// <summary>
    /// The issuer being down for a moment warns, rather than taking down the routes that need no
    /// token as well.
    /// </summary>
    [Fact]
    public async Task AKeyFetchThatFailsOnlyWarns()
    {
        var keys = Substitute.For<IJwtSigningKeySource>();
        var logger = new RecordingLogger();

        keys.GetKeys(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<SecurityKey>>>(_ =>
                throw new HttpRequestException("issuer is down")
            );

        Assert.True(
            await new JwtBearerStartupService().Startup(Provider(Configured(), keys, logger))
        );

        Assert.Contains("could not be fetched at startup", Assert.Single(logger.Warnings));
    }
}

/// <summary>
/// What <c>[JwtBearerAuthentication&lt;TScheme&gt;]</c> registers.
/// </summary>
public class JwtBearerAuthenticationAttributeTests
{
    private sealed class First : IAuthenticationScheme;

    private sealed class Second : IAuthenticationScheme;

    [Fact]
    public void TheSchemesSourceIsRegisteredWithThePackagesServices()
    {
        var services = new ServiceCollection();

        services.AddModules(new JwtBearerAuthenticationAttribute<First>().GetModule());

        Assert.Contains(
            services,
            service => service.ServiceType == typeof(IPrincipalSource<First>)
        );
        Assert.Contains(services, service => service.ServiceType == typeof(JwtBearerValidator));
        Assert.Contains(services, service => service.ServiceType == typeof(IJwtSigningKeySource));
    }

    /// <summary>
    /// Named twice, the scheme gets one source. Two schemes get one each.
    /// </summary>
    [Fact]
    public void AModuleIsEqualToAnotherForTheSameScheme()
    {
        var first = new JwtBearerAuthenticationAttribute<First>().GetModule();

        Assert.Equal(first, new JwtBearerAuthenticationAttribute<First>().GetModule());
        Assert.Equal(
            first.GetHashCode(),
            new JwtBearerAuthenticationAttribute<First>().GetModule().GetHashCode()
        );
        Assert.NotEqual(first, new JwtBearerAuthenticationAttribute<Second>().GetModule());
    }

    [Fact]
    public void TheSourceIsTheBearerSourceForTheScheme()
    {
        var services = new ServiceCollection();

        services.AddModules(new JwtBearerAuthenticationAttribute<First>().GetModule());
        services.AddSingleton(
            new JwtBearerValidator(
                Options.Create<IJwtBearerConfiguration>(new JwtBearerConfiguration()),
                Substitute.For<IJwtSigningKeySource>()
            )
        );

        var source = services.BuildServiceProvider().GetRequiredService<IPrincipalSource<First>>();

        Assert.IsType<BearerPrincipalSource<First>>(source);
    }
}
