using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;

namespace Hardened.Azure.Functions.Testing;

/// <summary>
/// Whether the two descriptions of an application's functions agree.
/// </summary>
/// <remarks>
/// <para>
/// The Worker SDK's build task scans the compiled assembly for <c>[Function]</c> methods and
/// writes <c>functions.metadata</c>; Hardened's generator writes an
/// <see cref="IFunctionMetadataProvider"/> from the handlers, before the shims exist as IL. The
/// host indexes whichever it is given - the provider, with worker indexing on - and both have to
/// say the same thing, or what the host runs is not what the build described.
/// </para>
/// <para>
/// One method every fixture calls, so the assertion is made once per family rather than once and
/// copied. It returns what disagrees rather than throwing, so the test framework's own assertion
/// reports it; an empty list is agreement.
/// </para>
/// </remarks>
public static class MetadataAgreement {
    /// <summary>
    /// Everything the provider and the build task's file disagree on, or nothing.
    /// </summary>
    /// <param name="provider">The generated provider, which is what the worker answers the host with.</param>
    /// <param name="directory">
    /// Where <c>functions.metadata</c> was written, or null for the test's own output directory,
    /// which is where a referenced application's copied files land.
    /// </param>
    public static async Task<IReadOnlyList<string>> Disagreements(
        IFunctionMetadataProvider provider, string? directory = null) {
        directory ??= AppContext.BaseDirectory;

        var declared = await provider.GetFunctionMetadataAsync(directory);

        var path = Path.Combine(directory, "functions.metadata");

        if (!File.Exists(path)) {
            return [$"The build task wrote no functions.metadata at {path}."];
        }

        using var written = JsonDocument.Parse(File.ReadAllBytes(path));

        var disagreements = new List<string>();

        var writtenNames = written.RootElement.EnumerateArray()
            .Select(function => function.GetProperty("name").GetString() ?? "")
            .Order(StringComparer.Ordinal)
            .ToArray();

        var declaredNames = declared.Select(function => function.Name ?? "").Order(StringComparer.Ordinal).ToArray();

        if (!writtenNames.SequenceEqual(declaredNames)) {
            disagreements.Add(
                $"The build task lists [{string.Join(", ", writtenNames)}] and the provider " +
                $"[{string.Join(", ", declaredNames)}].");

            return disagreements;
        }

        foreach (var function in written.RootElement.EnumerateArray()) {
            var name = function.GetProperty("name").GetString();
            var ours = declared.Single(one => one.Name == name);

            Compare(disagreements, name, "entryPoint", function, ours.EntryPoint);
            Compare(disagreements, name, "scriptFile", function, ours.ScriptFile);
            Compare(disagreements, name, "language", function, ours.Language);

            // Binding for binding, as JSON rather than as text: the host reads the document, so
            // key order and spacing are not part of the contract, and the values are.
            var expected = function.GetProperty("bindings").EnumerateArray()
                .Select(Canonical)
                .ToList();

            var actual = (ours.RawBindings ?? new List<string>())
                .Select(binding => {
                    using var document = JsonDocument.Parse(binding);

                    return Canonical(document.RootElement);
                })
                .ToList();

            if (!expected.SequenceEqual(actual)) {
                disagreements.Add(
                    $"{name}: the build task wrote bindings {string.Join(" ", expected)} and the " +
                    $"provider declares {string.Join(" ", actual)}.");
            }
        }

        return disagreements;
    }

    private static void Compare(
        List<string> disagreements, string? name, string property, JsonElement function, string? ours) {
        var written = function.TryGetProperty(property, out var value) ? value.GetString() : null;

        if (!string.Equals(written, ours, StringComparison.Ordinal)) {
            disagreements.Add($"{name}: {property} is \"{written}\" in functions.metadata and \"{ours}\" in the provider.");
        }
    }

    /// <summary>
    /// The element with its object keys sorted, so two documents that mean the same compare equal.
    /// </summary>
    private static string Canonical(JsonElement element) {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer)) {
            Write(element, writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer) {
        switch (element.ValueKind) {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject().OrderBy(one => one.Name, StringComparer.Ordinal)) {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }

                writer.WriteEndObject();

                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray()) {
                    Write(item, writer);
                }

                writer.WriteEndArray();

                break;

            default:
                element.WriteTo(writer);

                break;
        }
    }
}
