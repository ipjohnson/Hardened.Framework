using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.Application;

/// <summary>
/// Registers the environment an application runs in.
/// </summary>
/// <remarks>
/// <para>
/// <b>One object, two service types, and the second is why this exists.</b>
/// <see cref="IHardenedEnvironment"/> is what application code, configuration models and
/// <c>Matches</c> read. <see cref="IModuleEnvironment"/> is what the module system reads while it
/// is deciding what to register at all - <c>[IfEnvironment]</c>, <c>[IfNotEnvironment]</c> and
/// <c>IEnvironmentServiceCollectionConfiguration</c> are all answered from it.
/// </para>
/// <para>
/// <c>IHardenedEnvironment</c> derives from <c>IModuleEnvironment</c> and satisfies it, so a single
/// <see cref="EnvironmentImpl"/> can answer both. But a container is keyed on the type something
/// is registered under rather than on what it implements, so registering only the first leaves the
/// module system to look for the second, find nothing, and fall back to its own default - which
/// reads <c>ASPNETCORE_ENVIRONMENT</c> and answers <c>Production</c>.
/// </para>
/// <para>
/// The result was an application that was <c>development</c> to its own code and <c>Production</c>
/// to everything that decided which services existed, in the same process, with no diagnostic.
/// <c>guide/services</c>' own example - a console mail sender in development and an SMTP one
/// everywhere else - handed a developer the SMTP one. Registering both here is what makes the one
/// environment the documentation describes actually be one.
/// </para>
/// </remarks>
public static class HardenedEnvironmentServiceCollectionExtensions {

    /// <summary>
    /// Registers <paramref name="environment"/> as the application's environment.
    /// </summary>
    /// <remarks>
    /// A singleton instance rather than a factory, because the module system reads it while the
    /// collection is still being built and there is no provider to run a factory against.
    /// </remarks>
    public static IServiceCollection AddHardenedEnvironment(
        this IServiceCollection services, IHardenedEnvironment environment) {
        services.AddSingleton(environment);
        services.AddSingleton<IModuleEnvironment>(environment);

        return services;
    }

    /// <summary>
    /// Registers an <see cref="EnvironmentImpl"/> built from the process, carrying
    /// <paramref name="arguments"/>.
    /// </summary>
    /// <remarks>
    /// The name comes from <c>HARDENED_ENVIRONMENT</c>, or <c>development</c> when it is unset.
    /// This is the one-line form for a host that has nothing to say about its environment beyond
    /// the arguments it was started with.
    /// </remarks>
    public static IServiceCollection AddHardenedEnvironment(
        this IServiceCollection services, IReadOnlyList<string>? arguments = null) =>
        services.AddHardenedEnvironment(new EnvironmentImpl(arguments: arguments));
}
