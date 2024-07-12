namespace Hardened.SourceGenerator.Shared;

public static class ReadOnlySpanExtensions {
    public static bool StartsWith<T>(this ReadOnlySpan<T> span, T value) {
        return value != null &&
               !span.IsEmpty &&
               value.Equals(span[0]);
    }

    public static int IndexOf<T>(this ReadOnlySpan<T> span, T value, int startIndex) {
        if (value == null) throw new ArgumentNullException(nameof(value));

        for (var i = startIndex; i < span.Length; i++) {
            if (value.Equals(span[i])) {
                return i;
            }
        }

        return -1;
    }
}