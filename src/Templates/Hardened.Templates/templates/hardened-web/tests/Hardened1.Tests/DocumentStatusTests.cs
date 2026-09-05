using System.IO.Compression;
using System.Text.Json;

namespace Hardened1.Tests;

/// <summary>
/// The published document's status set, operation by operation, against what this suite
/// exercises.
/// </summary>
/// <remarks>
/// A declared status nothing answers - or an answered status the document never mentions - is the
/// defect class a reference page cannot reveal, because the page renders either way. The sets
/// below are the contract's declared statuses plus what the framework synthesizes and answers
/// itself: a 400 wherever generated validation or a non-string bound parameter can refuse a
/// request. Each listed status is driven by a test in this project, so the document, the
/// application and the suite have to agree or this fails naming the operation.
/// </remarks>
public class DocumentStatusTests {

    private static readonly Dictionary<string, string[]> Expected = new() {
#if (codeFirst && throwsMode)
        // Throws mode documents what the signature declares. The thrown NotFound and Conflict
        // answer at runtime and are invisible here - the declared models close exactly that gap.
        // The 400s stay: the id and the title are constrained, so binding either can refuse.
        ["GET /todos"] = ["200"],
        ["GET /todos/{id}"] = ["200", "400"],
        ["POST /todos"] = ["200", "400"],
        ["DELETE /todos/{id}"] = ["200", "400"],
#else
        // The declared statuses, plus the 400 the generated validation answers: the id and the
        // title are constrained, so every operation binding either can refuse.
        ["GET /todos"] = ["200"],
        ["GET /todos/{id}"] = ["200", "400", "404"],
        ["POST /todos"] = ["201", "400", "409"],
        ["DELETE /todos/{id}"] = ["204", "400", "404"],
#endif
    };

    [HardenedTest]
    public async Task EveryOperationDeclaresExactlyTheStatusesThisSuiteExercises(ITestWebApp app) {
        var document = await Document(app);
        var declared = new Dictionary<string, string[]>();

        foreach (var path in document.GetProperty("paths").EnumerateObject()) {
            foreach (var operation in path.Value.EnumerateObject()) {
                if (!operation.Value.TryGetProperty("responses", out var responses)) {
                    continue;
                }

                declared[$"{operation.Name.ToUpperInvariant()} {path.Name}"] = responses
                    .EnumerateObject()
                    .Select(response => response.Name)
                    .OrderBy(status => status, StringComparer.Ordinal)
                    .ToArray();
            }
        }

#if (xunit)
        Assert.Equal(
            Expected.Keys.OrderBy(key => key, StringComparer.Ordinal),
            declared.Keys.OrderBy(key => key, StringComparer.Ordinal));

        foreach (var operation in Expected) {
            Assert.Equal(operation.Value, declared[operation.Key]);
        }
#else
        Assert.That(
            declared.Keys.OrderBy(key => key, StringComparer.Ordinal),
            Is.EqualTo(Expected.Keys.OrderBy(key => key, StringComparer.Ordinal)));

        foreach (var operation in Expected) {
            Assert.That(declared[operation.Key], Is.EqualTo(operation.Value));
        }
#endif
    }

    /// <summary>The served document, which is stored and answered gzipped.</summary>
    private static async Task<JsonElement> Document(ITestWebApp app) {
        var response = await app.Get("/openapi.json");

        response.Assert.Ok();
        response.Body.Position = 0;

        await using var gzip = new GZipStream(response.Body, CompressionMode.Decompress);

        return JsonDocument.Parse(await new StreamReader(gzip).ReadToEndAsync()).RootElement;
    }
}
