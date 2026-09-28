using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Requests.Jwt.Testing;

/// <summary>
/// Makes the application under test trust <see cref="TestJwtIssuer"/> in place of its JWKS.
/// </summary>
/// <remarks>
/// <para>
/// The application's <see cref="JwtBearerConfiguration"/> still decides which tokens are accepted.
/// Where it sets no issuer or audience, <see cref="TestJwtIssuer.DefaultIssuer"/> and
/// <see cref="TestJwtIssuer.DefaultAudience"/> fill them, so a test needs no environment.
/// </para>
/// <para>
/// No key is fetched, so <c>JWT_AUTHORITY</c> and <c>JWT_JWKS_URL</c> are not needed.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly,
    AllowMultiple = false
)]
public sealed class JwtTestIssuerAttribute : Attribute, IHardenedTestDependencyRegistrationAttribute
{
    public void RegisterDependencies(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceCollection serviceCollection
    )
    {
        // Removed rather than registered after, so the application's key source is gone rather
        // than hidden by registration order.
        serviceCollection.RemoveAll<IJwtSigningKeySource>();
        serviceCollection.AddSingleton<TestJwtIssuer>();
        serviceCollection.AddSingleton<IJwtSigningKeySource>(provider =>
            provider.GetRequiredService<TestJwtIssuer>()
        );

        serviceCollection.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(
                Array.Empty<IConfigurationValueProvider>(),
                new IConfigurationValueAmender[]
                {
                    new SimpleConfigurationValueAmender<JwtBearerConfiguration>(
                        (_, configuration) =>
                        {
                            if (string.IsNullOrWhiteSpace(configuration.Issuer))
                            {
                                configuration.Issuer = TestJwtIssuer.DefaultIssuer;
                            }

                            if (string.IsNullOrWhiteSpace(configuration.Audience))
                            {
                                configuration.Audience = TestJwtIssuer.DefaultAudience;
                            }

                            return configuration;
                        }
                    ),
                }
            )
        );
    }
}
