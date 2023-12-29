using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Utilities;

public static class StringExtensions {
    public static StringValues ToStringValues(this string value) {
        var span = value.AsSpan();
        var commaCount = 0;

        for (var i = 0; i < span.Length; i++) {
            if (span[i] == ',') {
                commaCount++;
            }
        }

        if (commaCount == 0) {
            return new StringValues(value);
        }

        return SplitString(span, commaCount);
    }

    private static StringValues SplitString(ReadOnlySpan<char> span, int commaCount) {
        var stringArray = new string[commaCount + 1];
        var stringCount = 0;
        var currentStringIndex = 0;

        for (var i = 0; i < span.Length;) {
            if (span[i] == ',') {
                stringArray[stringCount] =
                    span.Slice(currentStringIndex, i - currentStringIndex).ToString();

                stringCount++;
                currentStringIndex = i + 1;

                if (currentStringIndex < span.Length - 1 &&
                    span[currentStringIndex] == ' ') {
                    currentStringIndex++;
                    i++;
                }
            }

            i++;
        }

        if (currentStringIndex < span.Length) {
            stringArray[stringCount] =
                span.Slice(currentStringIndex, span.Length - currentStringIndex).ToString();
        }
        else {
            stringArray[stringCount] = string.Empty;
        }

        return new StringValues(stringArray);
    }
}