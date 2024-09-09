namespace Hardened.Shared.Runtime.Application;

public interface IHardenedEnvironment {
    string Name { get; }

    IReadOnlyList<string> Arguments { get; }

    T? Value<T>(string name, T? defaultValue = default);
    
    T? CustomData<T>(string name, T? defaultValue = default);
}

public static class IEnvironmentExtensions {
    public static bool Matches(this IHardenedEnvironment environment, params string[] environments) {
        return environments.Any(s => environment.Name.Equals(s, StringComparison.CurrentCultureIgnoreCase));
    }

    public static bool MatchesVariable(this IHardenedEnvironment environment, string variable, string value) {
        return environment.Value(variable, "")!.Equals(value, StringComparison.CurrentCultureIgnoreCase);
    }
}