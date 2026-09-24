using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Runtime.Cors;

/// <summary>
/// The configuration <see cref="CorsAttribute{TPolicy}"/> answers with, registered by
/// <see cref="CorsServiceCollectionExtensions.AddCorsPolicy{TPolicy}"/>.
/// </summary>
/// <typeparam name="TPolicy">
/// Any type. It only names the policy, so an empty class declared for the purpose will do.
/// </typeparam>
public sealed class CorsPolicy<TPolicy>
{
    public CorsPolicy(CorsConfiguration configuration)
    {
        Configuration = configuration;
    }

    public CorsConfiguration Configuration { get; }
}

public static class CorsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the policy that <see cref="CorsAttribute{TPolicy}"/> answers with.
    /// </summary>
    /// <remarks>
    /// The policy starts from an empty <see cref="CorsConfiguration"/>. It does not read
    /// <c>CORS_ALLOWED_ORIGINS</c>, which configures the application's default policy only.
    /// </remarks>
    public static IServiceCollection AddCorsPolicy<TPolicy>(
        this IServiceCollection services,
        Action<CorsConfiguration> configure
    )
    {
        var configuration = new CorsConfiguration();

        configure(configuration);

        services.AddSingleton(new CorsPolicy<TPolicy>(configuration));

        return services;
    }
}
