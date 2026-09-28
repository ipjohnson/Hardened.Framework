using DependencyModules.Runtime;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Runtime.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Jwt;

/// <summary>
/// Authenticates a bearer token as a JWT, for <typeparamref name="TScheme"/>. Put it on the
/// application module:
/// </summary>
/// <code>
/// [HttpAuthenticationScheme("bearer", BearerFormat = "JWT")]
/// public sealed class BearerAuth : IAuthenticationScheme;
///
/// [HardenedModule]
/// [JwtBearerAuthentication&lt;BearerAuth&gt;]
/// public partial class Application;
/// </code>
/// <remarks>
/// Registers <see cref="BearerPrincipalSource{TScheme}"/> with <see cref="JwtBearerValidator"/>
/// as its validator, and loads <see cref="JwtBearerModule"/>. <see cref="JwtBearerConfiguration"/>
/// says which tokens are accepted.
/// </remarks>
/// <typeparam name="TScheme">
/// The scheme <c>[Authorize&lt;TScheme&gt;]</c> names on the handlers this protects.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class)]
public sealed class JwtBearerAuthenticationAttribute<TScheme> : Attribute, IDependencyModuleProvider
    where TScheme : IAuthenticationScheme
{
    public IDependencyModule GetModule() => new JwtBearerAuthenticationModule<TScheme>();
}

/// <summary>
/// What <see cref="JwtBearerAuthenticationAttribute{TScheme}"/> loads. Equal to any other for the
/// same scheme, so the source is registered once however many modules name it.
/// </summary>
internal sealed class JwtBearerAuthenticationModule<TScheme>
    : IDependencyModule,
        IServiceCollectionConfiguration
    where TScheme : IAuthenticationScheme
{
    public void PopulateServiceCollection(IServiceCollection services) => services.AddModules(this);

    public IEnumerable<IDependencyModule> GetModules() => [new JwtBearerModule()];

    public void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IPrincipalSource<TScheme>>(
            provider => new BearerPrincipalSource<TScheme>(
                provider.GetRequiredService<JwtBearerValidator>().Validate
            )
        );

    public override bool Equals(object? obj) => obj is JwtBearerAuthenticationModule<TScheme>;

    public override int GetHashCode() =>
        typeof(JwtBearerAuthenticationModule<TScheme>).GetHashCode();
}
