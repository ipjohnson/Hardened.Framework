namespace Hardened.Shared.Runtime.Collections;

public static class Extensions {
    public static void Foreach<T>(this IEnumerable<T> enumerable, Action<T> action) {
        foreach (var value in enumerable) {
            action(value);
        }
    }
}