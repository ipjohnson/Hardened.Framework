using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Testing.Impl;

public class TestEnvironment : IEnvironment {
    private readonly IDictionary<string, object> _values;

    public TestEnvironment(string name, IDictionary<string, object> values, IReadOnlyList<string>? arguments = null) {
        _values = values;
        Name = name;
        Arguments = arguments ?? Array.Empty<string>();
    }

    public string Name { get; }

    public IReadOnlyList<string> Arguments { get; }

    public T? Value<T>(string name, T? defaultValue = default) {
        if (_values.TryGetValue(name, out var value)) {
            if (value is T tValue) {
                return tValue;
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }

        return defaultValue;
    }
}