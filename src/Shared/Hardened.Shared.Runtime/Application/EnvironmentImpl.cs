namespace Hardened.Shared.Runtime.Application;

public class EnvironmentImpl : IEnvironment
{
    private readonly IDictionary<string, string>? _environmentValues;

    public EnvironmentImpl(string? name = null, IDictionary<string, string>? environmentValues = null)
    {
        Name = name ?? System.Environment.GetEnvironmentVariable("HARDENED_ENVIRONMENT") ?? "development";
        _environmentValues = environmentValues;
    }

    public string Name { get; }

    public T? Value<T>(string name, T? defaultValue = default)
    {
        string? envValue = null;

        _environmentValues?.TryGetValue(name, out envValue);

        if (string.IsNullOrEmpty(envValue))
        {
            envValue = Environment.GetEnvironmentVariable(name);
        }

        if (!string.IsNullOrEmpty(envValue))
        {
            if (typeof(T) == typeof(string))
            {
                return (T)(object)envValue;
            }

            return (T)Convert.ChangeType(envValue, typeof(T));
        }

        return defaultValue;
    }
}