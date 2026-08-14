using System.Collections;
using System.Reflection;
using System.Text;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Compares two spec models property by property, through reflection, and says where they differ.
/// </summary>
/// <remarks>
/// <para>
/// <b>The models' own <c>Equals</c> cannot be used for this.</b> They exist to let Roslyn cache
/// incremental generator stages, so they compare the fields that decide whether regeneration is
/// needed and skip the rest - <c>PropertyModel.Equals</c> ignores <c>EnumValues</c>,
/// <c>IsDictionary</c>, <c>DictionaryValueType</c>, <c>DictionaryValueRef</c> and all three
/// <c>ArrayItems*</c> fields. A round-trip test built on <c>Equals</c> would pass while silently
/// dropping every one of them, and the damage would surface as wrong generated types rather than as
/// a failing test.
/// </para>
/// <para>
/// Reflection rather than a hand-written comparer for the same reason: a field added to a model and
/// forgotten in the serializer has to fail here without anyone remembering to extend this file.
/// </para>
/// </remarks>
internal static class DeepEquality {

    public static void AssertEqual(object? expected, object? actual) {
        var differences = new List<string>();
        Compare(expected, actual, "model", differences, new HashSet<object>(ReferenceEqualityComparer.Instance));

        if (differences.Count == 0) {
            return;
        }

        var message = new StringBuilder("Round trip lost or changed data:");

        foreach (var difference in differences) {
            message.Append("\n  ").Append(difference);
        }

        throw new Xunit.Sdk.XunitException(message.ToString());
    }

    private static void Compare(object? expected, object? actual, string path, List<string> differences, HashSet<object> seen) {
        if (expected is null || actual is null) {
            if (!ReferenceEquals(expected, actual)) {
                differences.Add($"{path}: expected {Describe(expected)}, found {Describe(actual)}");
            }

            return;
        }

        var type = expected.GetType();

        if (type != actual.GetType()) {
            differences.Add($"{path}: expected type {type.Name}, found {actual.GetType().Name}");
            return;
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)) {
            if (!Equals(expected, actual)) {
                differences.Add($"{path}: expected {Describe(expected)}, found {Describe(actual)}");
            }

            return;
        }

        // Guards against a model that ever gains a cycle; today none has one.
        if (!seen.Add(expected)) {
            return;
        }

        if (expected is IDictionary expectedMap) {
            CompareDictionaries(expectedMap, (IDictionary)actual, path, differences, seen);
            return;
        }

        if (expected is IEnumerable expectedItems) {
            CompareSequences(expectedItems, (IEnumerable)actual, path, differences, seen);
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is null) {
                continue;
            }

            Compare(property.GetValue(expected), property.GetValue(actual), $"{path}.{property.Name}", differences, seen);
        }
    }

    private static void CompareDictionaries(IDictionary expected, IDictionary actual, string path, List<string> differences, HashSet<object> seen) {
        if (expected.Count != actual.Count) {
            differences.Add($"{path}: expected {expected.Count} entries, found {actual.Count}");
            return;
        }

        foreach (DictionaryEntry entry in expected) {
            if (!actual.Contains(entry.Key)) {
                differences.Add($"{path}: missing key '{entry.Key}'");
                continue;
            }

            Compare(entry.Value, actual[entry.Key], $"{path}['{entry.Key}']", differences, seen);
        }
    }

    private static void CompareSequences(IEnumerable expected, IEnumerable actual, string path, List<string> differences, HashSet<object> seen) {
        var expectedList = expected.Cast<object?>().ToList();
        var actualList = actual.Cast<object?>().ToList();

        if (expectedList.Count != actualList.Count) {
            differences.Add($"{path}: expected {expectedList.Count} items, found {actualList.Count}");
            return;
        }

        for (var i = 0; i < expectedList.Count; i++) {
            Compare(expectedList[i], actualList[i], $"{path}[{i}]", differences, seen);
        }
    }

    /// <summary>
    /// Null and empty print differently on purpose - telling them apart is most of what this test
    /// exists to do.
    /// </summary>
    private static string Describe(object? value) => value switch {
        null => "<null>",
        string { Length: 0 } => "<empty string>",
        string text => $"\"{text}\"",
        _ => value.ToString() ?? "<null>",
    };
}
