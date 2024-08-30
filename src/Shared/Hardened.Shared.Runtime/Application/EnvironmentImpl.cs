namespace Hardened.Shared.Runtime.Application;

public class EnvironmentImpl : IEnvironment {
    private readonly IDictionary<string, string>? _environmentValues;
    private readonly IDictionary<string, object>? _customData;

    public EnvironmentImpl(string? name = null,
        IDictionary<string, string>? environmentValues = null,
        IReadOnlyList<string>? arguments = null,
        IDictionary<string, object>? customData = null) {
        Name = name ?? System.Environment.GetEnvironmentVariable("HARDENED_ENVIRONMENT") ?? "development";
        _environmentValues = environmentValues;
        _customData = customData;
        Arguments = arguments ?? Array.Empty<string>();
        
    }

    public string Name { get; }

    public IReadOnlyList<string> Arguments { get; }

    public T? Value<T>(string name, T? defaultValue = default) {
        string? envValue = null;

        _environmentValues?.TryGetValue(name, out envValue);

        if (string.IsNullOrEmpty(envValue)) {
            envValue = Environment.GetEnvironmentVariable(name);
        }

        if (!string.IsNullOrEmpty(envValue)) {
            if (typeof(T) == typeof(string)) {
                return (T)(object)envValue;
            }

            return (T)Convert.ChangeType(envValue, typeof(T));
        }

        return defaultValue;
    }

    public T? CustomData<T>(string name, T? defaultValue = default) {
        if (_customData != null && _customData.TryGetValue(name, out var value)) {
            return (T)value;
        }
        
        return defaultValue;
    }
}