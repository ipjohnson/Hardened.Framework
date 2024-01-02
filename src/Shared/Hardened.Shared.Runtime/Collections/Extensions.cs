namespace Hardened.Shared.Runtime.Collections;

public static class Extensions {
    public static void Foreach<T>(this IEnumerable<T> enumerable, Action<T> action) {
        foreach (var value in enumerable) {
            action(value);
        }
    }

    public static TValue GetOrDefault<TKey, TValue>(
        this IDictionary<TKey, TValue> dictionary,
        TKey key,
        TValue defaultValue = default(TValue)) {

        if (dictionary.TryGetValue(key, out var value)) {
            return value;
        }
        
        return defaultValue;
    }
}