using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Testing.Impl;

public class TestEnvironment : IEnvironment {
    private readonly IDictionary<string, object> _values;
    private readonly IDictionary<string, object>? _customData;

    public TestEnvironment(string name, 
        IDictionary<string, object> values, 
        IReadOnlyList<string>? arguments = null,
        IDictionary<string, object>? customData = null) {
        _values = values;
        Name = name;
        _customData = customData;
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

    public T? CustomData<T>(string name, T? defaultValue = default) {
        if (_customData != null && _customData.TryGetValue(name, out var value)) {
            return (T)value;
        }
        
        return defaultValue;
    }
}