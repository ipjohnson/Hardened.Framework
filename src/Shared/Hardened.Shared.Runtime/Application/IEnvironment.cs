using DependencyModules.Runtime.Interfaces;

namespace Hardened.Shared.Runtime.Application;

public interface IHardenedEnvironment : IModuleEnvironment {
    string Name { get; }

    IReadOnlyList<string> Arguments { get; }

    T? Value<T>(string name, T? defaultValue = default);

    T? CustomData<T>(string name, T? defaultValue = default);

    string IModuleEnvironment.EnvironmentName => Name;

    string? IModuleEnvironment.Value(string name) => Value<string>(name);
}

public static class IEnvironmentExtensions {
    public static bool Matches(this IHardenedEnvironment environment, params string[] environments) {
        return environments.Any(s => environment.Name.Equals(s, StringComparison.CurrentCultureIgnoreCase));
    }

    public static bool MatchesVariable(this IHardenedEnvironment environment, string variable, string value) {
        return environment.Value(variable, "")!.Equals(value, StringComparison.CurrentCultureIgnoreCase);
    }
}