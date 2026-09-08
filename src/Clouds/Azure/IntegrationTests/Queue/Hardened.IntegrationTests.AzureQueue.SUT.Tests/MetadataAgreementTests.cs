using System.Text.Json;
using Hardened.IntegrationTests.AzureQueue.SUT.Generated;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;
using Xunit;

namespace Hardened.IntegrationTests.AzureQueue.SUT.Tests;

/// <summary>
/// The two descriptions of this application's functions agree.
///
/// <para>
/// The Worker SDK's build task scans the compiled assembly for <c>[Function]</c> methods and writes
/// <c>functions.metadata</c>; Hardened's generator writes an <c>IFunctionMetadataProvider</c> from
/// the handlers, before the shims exist as IL. The host indexes whichever it is given - the
/// provider, with worker indexing on - and both have to say the same thing, or what the host runs
/// is not what the build described. This is the assertion spike S1 was written to make.
/// </para>
/// </summary>
public class MetadataAgreementTests {

    /// <summary>
    /// What the generated provider returns, which is what the worker answers the host with.
    /// </summary>
    private static async Task<IReadOnlyList<IFunctionMetadata>> Declared() =>
        await new AzureQueueTestAppAzureFunctionMetadataProvider()
            .GetFunctionMetadataAsync(AppContext.BaseDirectory);

    /// <summary>
    /// What the build task wrote. It reaches this directory because the application copies it to
    /// its output and a referenced project's copied files flow to the referencing one.
    /// </summary>
    private static JsonDocument Written() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "functions.metadata")));

    [Fact]
    public async Task TheProviderAndTheBuildTaskListTheSameFunctions() {
        var declared = await Declared();

        using var written = Written();

        Assert.Equal(
            written.RootElement.EnumerateArray().Select(function => function.GetProperty("name").GetString()).Order(),
            declared.Select(function => function.Name).Order());
    }

    [Theory]
    [InlineData("entryPoint")]
    [InlineData("scriptFile")]
    [InlineData("language")]
    public async Task EveryFunctionAgreesOn(string property) {
        var declared = await Declared();

        using var written = Written();

        foreach (var function in written.RootElement.EnumerateArray()) {
            var name = function.GetProperty("name").GetString();
            var ours = Assert.Single(declared, one => one.Name == name);

            var expected = function.GetProperty(property).GetString();

            var actual = property switch {
                "entryPoint" => ours.EntryPoint,
                "scriptFile" => ours.ScriptFile,
                _ => ours.Language
            };

            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// Binding for binding, as JSON rather than as text: the host reads the document, so key order
    /// and spacing are not part of the contract, and the values are.
    /// </summary>
    [Fact]
    public async Task EveryFunctionAgreesOnItsBindings() {
        var declared = await Declared();

        using var written = Written();

        foreach (var function in written.RootElement.EnumerateArray()) {
            var name = function.GetProperty("name").GetString();
            var ours = Assert.Single(declared, one => one.Name == name);

            var expected = function.GetProperty("bindings").EnumerateArray()
                .Select(binding => Canonical(binding))
                .ToList();

            var actual = (ours.RawBindings ?? new List<string>())
                .Select(binding => {
                    using var document = JsonDocument.Parse(binding);

                    return Canonical(document.RootElement);
                })
                .ToList();

            Assert.Equal(expected, actual);
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

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
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
